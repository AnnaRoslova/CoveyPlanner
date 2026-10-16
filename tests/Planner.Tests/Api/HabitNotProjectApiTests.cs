using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Привычка ≠ проект (docs/specs/fixes-2026-10.md, A1–A2): у постоянной цели проект сам не создаётся — только выбранный.</summary>
public class HabitNotProjectApiTests : ApiTestBase
{
    private async Task<WeeklyGoalDto> CreateRecurringAsync(string title)
    {
        var roleId = (await GetBoardAsync()).Roles.Single(r => r.Name == "Ум").Id;
        return await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Monday, title, true));
    }

    private Task<HttpResponseMessage> AddGoalStepRawAsync(int goalId, string title) =>
        Client.PostAsJsonAsync($"/api/goals/{goalId}/steps", new StepRequest(title));

    private async Task<int> ProjectsCountAsync() =>
        (await GetBoardAsync()).SomedayTasks.Count(t => t.IsProject);

    [Fact]
    public async Task A1_RecurringGoalWithoutProject_StepFromGoalAndFromSession_Refused_NoProjectCreated()
    {
        var goal = await CreateRecurringAsync("Чтение");
        Assert.True(goal.IsRecurring);
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(Monday, 0))).Task!;

        var fromGoal = await AddGoalStepRawAsync(goal.Id, "Глава 1");
        var fromSession = await Client.PostAsJsonAsync($"/api/tasks/{session.Id}/steps", new StepRequest("Глава 1"));

        Assert.Equal(HttpStatusCode.BadRequest, fromGoal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fromSession.StatusCode);
        Assert.Contains("выберите проект", await fromGoal.Content.ReadAsStringAsync());
        Assert.Equal(0, await ProjectsCountAsync());
        Assert.Null((await GetWeekAsync()).Goals.Single(g => g.Id == goal.Id).ProjectId);
    }

    [Fact]
    public async Task A1_RecurringGoal_ChosenProjectInTrash_NewProjectNotCreated()
    {
        var goal = await CreateRecurringAsync("Чтение");
        var book = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Атомные привычки", new SomedayTargetDto(null, 0), IsProject: true));
        await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, 1, null, null, null, null, IsRecurring: true, ProjectId: book.Id));
        (await Client.DeleteAsync($"/api/tasks/{book.Id}")).EnsureSuccessStatusCode();

        var response = await AddGoalStepRawAsync(goal.Id, "Глава 1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ProjectsCountAsync());
    }

    [Fact]
    public async Task A2_RecurringGoal_ChosenProject_StepGoesThere()
    {
        var goal = await CreateRecurringAsync("Чтение");
        var book = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Атомные привычки", new SomedayTargetDto(null, 0), IsProject: true));
        await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, 1, null, null, null, null, IsRecurring: true, ProjectId: book.Id));

        var added = await PostAsync<GoalStepResultDto>($"/api/goals/{goal.Id}/steps", new StepRequest("Глава 1"));

        Assert.Null(added.CreatedProject);
        Assert.Equal(book.Id, added.Step.ParentId);
    }

    [Fact]
    public async Task A1_SeriesEndsThisWeek_GoalIsOneOff_FirstStepCreatesProject()
    {
        var goal = await CreateRecurringAsync("Чтение");
        var updated = (await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, 1, null, null, null, null, IsRecurring: false))).Goal;
        Assert.False(updated.IsRecurring);

        var added = await PostAsync<GoalStepResultDto>($"/api/goals/{goal.Id}/steps", new StepRequest("Глава 1"));

        Assert.NotNull(added.CreatedProject); // разовая — G3 как было
    }
}
