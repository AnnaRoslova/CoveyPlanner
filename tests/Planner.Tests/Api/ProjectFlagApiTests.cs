using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// «🛠 Проекты» (docs/specs/next-batch.md, N6): проект — дело с явным флагом IsProject. Флаг ставится кнопкой и любым появлением шага,
/// снимается только явно и только без шагов; шаг и занятие цели проектом не бывают, проект шагом — тоже.
/// </summary>
public class ProjectFlagApiTests : ApiTestBase
{
    private Task<TaskDto> SetProjectAsync(int id, bool isProject) =>
        PutAsync<TaskDto>($"/api/tasks/{id}/project", new SetProjectRequest(isProject));

    private async Task<HttpStatusCode> TrySetProjectAsync(int id, bool isProject) =>
        (await Client.PutAsJsonAsync($"/api/tasks/{id}/project", new SetProjectRequest(isProject))).StatusCode;

    private Task<StepDto> AddStepAsync(int taskId, string title) =>
        PostAsync<StepDto>($"/api/tasks/{taskId}/steps", new StepRequest(title));

    private Task<TaskDto> ThoughtAsync(string title, int? categoryId = null) =>
        CreateTaskAsync(title, new SomedayTargetDto(categoryId, 0));

    private async Task<TaskDto> FeedTaskAsync(int id) => (await GetBoardAsync()).SomedayTasks.Single(t => t.Id == id);

    [Fact]
    public async Task N6_3_MakeProject_ThoughtStaysSomedayWithCategory_FlagOnBoard()
    {
        var books = await CreateCategoryAsync("Книги");
        var thought = await ThoughtAsync("Атомные привычки", books.Id);

        var project = await SetProjectAsync(thought.Id, true);

        Assert.True(project.IsProject);
        var onBoard = await FeedTaskAsync(thought.Id);
        Assert.Equal((TaskStateDto.Someday, books.Id, true), (onBoard.State, onBoard.CategoryId, onBoard.IsProject));
    }

    [Fact]
    public async Task N6_6_FirstStep_MakesProject()
    {
        var thought = await ThoughtAsync("Ремонт");

        await AddStepAsync(thought.Id, "Стены");

        Assert.True((await FeedTaskAsync(thought.Id)).IsProject);
    }

    [Fact]
    public async Task N6_6_ProjectField_MakesTargetProject_ProjectCannotBecomeStep()
    {
        var repair = await ThoughtAsync("Ремонт");
        var paint = await ThoughtAsync("Краска");
        var move = await ThoughtAsync("Переезд");

        await PutAsync<TaskDto>($"/api/tasks/{paint.Id}/parent", new SetParentRequest(repair.Id));
        Assert.True((await FeedTaskAsync(repair.Id)).IsProject);

        // Проект без шагов — всё равно проект: шагом не становится
        await SetProjectAsync(move.Id, true);
        var response = await Client.PutAsJsonAsync($"/api/tasks/{move.Id}/parent", new SetParentRequest(repair.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task N6_7_Unmake_OnlyWithoutLiveSteps_RestoredStepMakesProjectAgain()
    {
        var repair = await ThoughtAsync("Ремонт");
        var walls = await AddStepAsync(repair.Id, "Стены");

        Assert.Equal(HttpStatusCode.BadRequest, await TrySetProjectAsync(repair.Id, false));

        (await Client.DeleteAsync($"/api/tasks/{walls.Id}")).EnsureSuccessStatusCode();
        var thought = await SetProjectAsync(repair.Id, false);
        Assert.Equal((false, TaskStateDto.Someday), (thought.IsProject, thought.State));

        await PostAsync<TaskDto>($"/api/trash/{walls.Id}/restore", new { });
        Assert.True((await FeedTaskAsync(repair.Id)).IsProject);
    }

    [Fact]
    public async Task N6_6_StepAndSession_AreNeverProjects()
    {
        var repair = await ThoughtAsync("Ремонт");
        var walls = await AddStepAsync(repair.Id, "Стены");
        Assert.Equal(HttpStatusCode.BadRequest, await TrySetProjectAsync(walls.Id, true));

        var roleId = (await GetBoardAsync()).Roles.First().Id;
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Monday, "Бег"));
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(Monday, 0))).Task!;
        Assert.Equal(HttpStatusCode.BadRequest, await TrySetProjectAsync(session.Id, true));
    }

    [Fact]
    public async Task N6_9_Board_HasPlannedProjectsOfAnyWeek_WithSteps()
    {
        var farDay = Monday.AddDays(21);
        var move = await CreateInDayAsync("Переезд", farDay);
        await AddStepAsync(move.Id, "Коробки");
        var plain = await CreateInDayAsync("Просто дело", farDay);

        var board = await GetBoardAsync();

        var projects = board.PlannedProjects!;
        Assert.Contains(projects, p => p.Id == move.Id && p.IsProject && p.PlannedDate == farDay);
        Assert.DoesNotContain(projects, p => p.Id == plain.Id);
        Assert.Contains(board.Steps!, s => s.ParentId == move.Id && s.Title == "Коробки");
    }

    [Fact]
    public async Task N6_9_DoneProject_NotOnBoard()
    {
        var move = await CreateInDayAsync("Переезд");
        await SetProjectAsync(move.Id, true);
        await SetTaskDoneAsync(move.Id, true);

        Assert.DoesNotContain((await GetBoardAsync()).PlannedProjects!, p => p.Id == move.Id);
    }

    [Fact]
    public async Task N6_11_CreateAsProject()
    {
        var task = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Курс", new SomedayTargetDto(null, 0), IsProject: true));

        Assert.True(task.IsProject);
        Assert.Equal(TaskStateDto.Someday, task.State);
    }

    [Fact]
    public async Task N6_4_GoalStepCreatesProject_FlaggedAndUnsorted()
    {
        var roleId = (await GetBoardAsync()).Roles.First().Id;
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Monday, "Подготовка к собесу"));

        var result = await PostAsync<GoalStepResultDto>($"/api/goals/{goal.Id}/steps", new StepRequest("Резюме"));

        Assert.True(result.CreatedProject!.IsProject);
        Assert.Null(result.CreatedProject.CategoryId);
    }

    [Fact]
    public async Task N6_13_ChosenGoalProject_BecomesProject()
    {
        var roleId = (await GetBoardAsync()).Roles.First().Id;
        var book = await ThoughtAsync("Атомные привычки");
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Monday, "Чтение"));

        await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, goal.TimesPerWeek, null, null, null, null, ProjectId: book.Id));

        Assert.True((await FeedTaskAsync(book.Id)).IsProject);
    }
}
