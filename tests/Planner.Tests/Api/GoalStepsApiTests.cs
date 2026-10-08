using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Один список шагов на цель (docs/specs/tasks-steps-done.md, G1–G10, К28–К36): шаги цели — шаги её проекта.</summary>
public class GoalStepsApiTests : ApiTestBase
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);

    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private async Task<WeeklyGoalDto> CreateGoalAsync(string title, bool recurring = false, string role = "Работа") =>
        await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync(role), Monday, title, recurring));

    private Task<GoalStepResultDto> AddGoalStepAsync(int goalId, string title) =>
        PostAsync<GoalStepResultDto>($"/api/goals/{goalId}/steps", new StepRequest(title));

    private async Task<TaskDto> ScheduleAsync(WeeklyGoalDto goal, DateOnly day) =>
        (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(day, 0))).Task!;

    private Task<List<StepDto>> StepsAsync(int taskId) => GetAsync<List<StepDto>>($"/api/tasks/{taskId}/steps");

    private async Task<WeeklyGoalDto> GoalAsync(int id, DateOnly? week = null) =>
        (await GetWeekAsync(week)).Goals.Single(g => g.Id == id);

    [Fact]
    public async Task G3_K28_FirstStep_CreatesProjectInUnsorted_WithGoalTitleAndRoles_SelectedInGoal()
    {
        var work = await RoleIdAsync("Работа");
        var goal = await CreateGoalAsync("Подготовка к собесу");

        var added = await AddGoalStepAsync(goal.Id, "Задачи");
        var project = added.CreatedProject!;
        Assert.Equal(("Подготовка к собесу", TaskStateDto.Someday, (int?)null), (project.Title, project.State, project.CategoryId));
        Assert.Equal([work], project.RoleIds);
        Assert.Equal((project.Id, "Подготовка к собесу"), (added.Goal.ProjectId!.Value, added.Goal.ProjectTitle));
        Assert.Equal(project.Id, added.Step.ParentId);

        // Второй шаг — в тот же проект, нового не создаётся
        var second = await AddGoalStepAsync(goal.Id, "Резюме");
        Assert.Null(second.CreatedProject);
        Assert.Equal(["Задачи", "Резюме"], (await StepsAsync(project.Id)).Select(s => s.Title));
        Assert.Equal("Подготовка к собесу", Assert.Single((await GetBoardAsync()).SomedayTasks).Title);
    }

    [Fact]
    public async Task G4_K29_K36_StepAddedToSession_GoesToGoalProject_SessionNotAProject()
    {
        var goal = await CreateGoalAsync("Подготовка к собесу");
        var tue = await ScheduleAsync(goal, Tuesday);
        var wed = await ScheduleAsync(goal, Wednesday);

        var step = await PostAsync<StepDto>($"/api/tasks/{tue.Id}/steps", new StepRequest("Задачи"));
        var projectId = (await GoalAsync(goal.Id)).ProjectId!.Value;
        Assert.Equal(projectId, step.ParentId);
        await PostAsync<StepDto>($"/api/tasks/{wed.Id}/steps", new StepRequest("Резюме"));
        Assert.Equal(["Задачи", "Резюме"], (await StepsAsync(projectId)).Select(s => s.Title));
        Assert.Empty(await StepsAsync(tue.Id));

        // Занятие не бывает проектом
        var other = await CreateInDayAsync("Звонок");
        var response = await Client.PutAsJsonAsync($"/api/tasks/{other.Id}/parent", new SetParentRequest(wed.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Неделя отдаёт шаги проекта цели
        Assert.Equal(2, (await GetWeekAsync()).Steps!.Count(s => s.ParentId == projectId));
    }

    [Fact]
    public async Task G5_K31_StepsToggle_SessionAndGoalUntouched_LastStepCompletesProjectOnly()
    {
        var goal = await CreateGoalAsync("Подготовка к собесу");
        var session = await ScheduleAsync(goal, Tuesday);
        var a = (await AddGoalStepAsync(goal.Id, "Задачи")).Step;
        var b = (await AddGoalStepAsync(goal.Id, "Резюме")).Step;
        var projectId = a.ParentId!.Value;

        await SetTaskDoneAsync(a.Id, true);
        var week = await GetWeekAsync();
        Assert.Equal(TaskStateDto.Planned, week.Tasks.Single(t => t.Id == session.Id).State);
        Assert.Equal(0, week.Goals.Single(g => g.Id == goal.Id).DoneCount);

        var result = await PutAsync<TaskDoneResultDto>($"/api/tasks/{b.Id}/done", new SetDoneRequest(true));
        Assert.Equal((projectId, TaskStateDto.Done), (result.Parent!.Id, result.Parent.State));
        week = await GetWeekAsync();
        Assert.Equal(TaskStateDto.Planned, week.Tasks.Single(t => t.Id == session.Id).State);
        Assert.Equal(0, week.Goals.Single(g => g.Id == goal.Id).DoneCount);
    }

    [Fact]
    public async Task G1_G7_K32_K33_RecurringGoal_SameProjectNextWeek_DeleteThisWeekKeepsProject()
    {
        var goal = await CreateGoalAsync("Чтение", recurring: true, role: "Ум");
        var projectId = (await AddGoalStepAsync(goal.Id, "Глава 1")).Step.ParentId!.Value;

        var next = (await GetWeekAsync(Monday.AddDays(7))).Goals.Single(g => g.SeriesId == goal.SeriesId);
        Assert.Equal(projectId, next.ProjectId);
        await AddGoalStepAsync(next.Id, "Глава 2");
        Assert.Equal(["Глава 1", "Глава 2"], (await StepsAsync(projectId)).Select(s => s.Title));

        (await Client.DeleteAsync($"/api/goals/{goal.Id}")).EnsureSuccessStatusCode();
        Assert.Contains((await GetBoardAsync()).SomedayTasks, t => t.Id == projectId);
        Assert.Equal(2, (await StepsAsync(projectId)).Count);
        Assert.Equal(projectId, (await GetWeekAsync(Monday.AddDays(14))).Goals.Single(g => g.SeriesId == goal.SeriesId).ProjectId);
    }

    /// <summary>R2: будущие недели постоянной цели созданы раньше, чем у неё появился проект, — проект проставляется и им.</summary>
    [Fact]
    public async Task R2_FutureWeeksGeneratedBefore_GetProjectOnFirstStep()
    {
        var goal = await CreateGoalAsync("Чтение", recurring: true, role: "Ум");
        var next = (await GetWeekAsync(Monday.AddDays(7))).Goals.Single(g => g.SeriesId == goal.SeriesId);
        await GetWeekAsync(Monday.AddDays(14)); // и эту неделю — тоже заранее
        Assert.Null(next.ProjectId);

        var projectId = (await AddGoalStepAsync(goal.Id, "Глава 1")).Step.ParentId!.Value;

        foreach (var week in new[] { Monday.AddDays(7), Monday.AddDays(14) })
        {
            var w = await GetWeekAsync(week);
            Assert.Equal(projectId, w.Goals.Single(g => g.SeriesId == goal.SeriesId).ProjectId);
            Assert.Contains(w.Steps!, s => s.Title == "Глава 1"); // и шаги приходят с той неделей
        }
    }

    /// <summary>R4: последний шаг отметили «из занятия» — отмечены проект и это занятие; без занятия — только проект.</summary>
    [Fact]
    public async Task R4_LastStepFromSession_CompletesProjectAndThatSession()
    {
        var goal = await CreateGoalAsync("Подготовка к собесу");
        var tue = await ScheduleAsync(goal, Tuesday);
        var wed = await ScheduleAsync(goal, Wednesday);
        var a = (await AddGoalStepAsync(goal.Id, "Задачи")).Step;
        var b = (await AddGoalStepAsync(goal.Id, "Резюме")).Step;

        // Не последний шаг — занятие не трогается даже «из занятия»
        var first = await PutAsync<TaskDoneResultDto>($"/api/tasks/{a.Id}/done", new SetDoneRequest(true, tue.Id));
        Assert.Null(first.Session);

        var last = await PutAsync<TaskDoneResultDto>($"/api/tasks/{b.Id}/done", new SetDoneRequest(true, tue.Id));
        Assert.Equal(TaskStateDto.Done, last.Parent!.State);
        Assert.Equal((tue.Id, TaskStateDto.Done), (last.Session!.Id, last.Session.State));
        var week = await GetWeekAsync();
        Assert.Equal(TaskStateDto.Planned, week.Tasks.Single(t => t.Id == wed.Id).State); // другое занятие — нет
        Assert.Equal(1, week.Goals.Single(g => g.Id == goal.Id).DoneCount);
    }

    [Fact]
    public async Task R4_LastStepFromGoalWindow_OnlyProject()
    {
        var goal = await CreateGoalAsync("Подготовка к собесу");
        var tue = await ScheduleAsync(goal, Tuesday);
        var a = (await AddGoalStepAsync(goal.Id, "Задачи")).Step;
        var done = await PutAsync<TaskDoneResultDto>($"/api/tasks/{a.Id}/done", new SetDoneRequest(true));
        Assert.Equal(TaskStateDto.Done, done.Parent!.State);
        Assert.Null(done.Session);
        Assert.Equal(TaskStateDto.Planned, (await GetWeekAsync()).Tasks.Single(t => t.Id == tue.Id).State);
    }

    [Fact]
    public async Task G8_G9_K34_K35_DoneProject_StepsGoThere_TrashedProject_NewOneCreated()
    {
        var goal = await CreateGoalAsync("Подготовка к собесу");
        var projectId = (await AddGoalStepAsync(goal.Id, "Задачи")).Step.ParentId!.Value;

        await SetTaskDoneAsync(projectId, true);
        var added = await AddGoalStepAsync(goal.Id, "Резюме");
        Assert.Null(added.CreatedProject);
        Assert.Equal(projectId, added.Step.ParentId);
        Assert.False(added.Goal.ProjectIsOpen); // выполнен — пометка в окне
        Assert.Null((await ScheduleAsync(goal, Tuesday)).ParentId); // новые занятия — без проекта (P3)

        await SetTaskDoneAsync(projectId, false);
        (await Client.DeleteAsync($"/api/tasks/{projectId}")).EnsureSuccessStatusCode();
        var fresh = await AddGoalStepAsync(goal.Id, "Вопросы");
        Assert.NotNull(fresh.CreatedProject);
        Assert.NotEqual(projectId, fresh.CreatedProject!.Id);
        Assert.Equal(fresh.CreatedProject.Id, fresh.Goal.ProjectId);
    }
}
