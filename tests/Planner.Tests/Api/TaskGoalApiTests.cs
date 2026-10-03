using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Дело → занятие цели (у дела — одна цель): перетаскивание дела на строку цели.</summary>
public class TaskGoalApiTests : ApiTestBase
{
    private async Task<RoleDto> RoleAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name);

    private async Task<WeeklyGoalDto> GoalAsync(string role, string title, int? quadrant = null)
    {
        var r = await RoleAsync(role);
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(r.Id, Monday, title, IsRecurring: false));
        return (await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(title, [r.Id], 3, null, quadrant, null, null))).Goal;
    }

    private Task<HttpResponseMessage> SetGoalRawAsync(int taskId, int? goalId) =>
        Client.PutAsJsonAsync($"/api/tasks/{taskId}/goal", new SetTaskGoalRequest(goalId));

    private Task<TaskGoalResultDto> SetGoalAsync(int taskId, int? goalId) =>
        PutAsync<TaskGoalResultDto>($"/api/tasks/{taskId}/goal", new SetTaskGoalRequest(goalId));

    private async Task<WeeklyGoalDto> ReloadAsync(WeeklyGoalDto goal) => (await GetWeekAsync()).Goals.Single(g => g.Id == goal.Id);

    [Fact]
    public async Task Link_TaskBecomesSession_RolesAndQuadrantFromGoal()
    {
        var nature = await GoalAsync("Дух", "Побыть на природе", quadrant: 2);
        var walk = await CreateInDayAsync("Прогулка в парке");

        var result = await SetGoalAsync(walk.Id, nature.Id);

        Assert.Equal(nature.Id, result.Task.GoalId);
        Assert.Equal(nature.RoleIds, result.Task.RoleIds);
        Assert.Equal(2, result.Task.Quadrant);
        Assert.Empty(result.ReplacedTaskIds);
        Assert.Equal([walk.Id], (await ReloadAsync(nature)).Sessions.Select(s => s.TaskId));
    }

    [Fact]
    public async Task LinkToOtherGoal_SessionMoves_OldGoalLosesIt()
    {
        var steps = await GoalAsync("Тело", "10к шагов");
        var nature = await GoalAsync("Дух", "Побыть на природе");
        var walk = await CreateInDayAsync("Прогулка в парке");
        await SetGoalAsync(walk.Id, steps.Id);
        await PutAsync<TaskDto>($"/api/tasks/{walk.Id}/done", new SetDoneRequest(true));

        var result = await SetGoalAsync(walk.Id, nature.Id);

        Assert.Equal(nature.Id, result.Task.GoalId);
        Assert.Equal(nature.RoleIds, result.Task.RoleIds);
        Assert.Equal(2, result.Goals.Count); // прежняя и новая — с новым прогрессом
        Assert.Equal(0, (await ReloadAsync(steps)).DoneCount);
        Assert.Equal(1, (await ReloadAsync(nature)).DoneCount);
        Assert.Single((await GetWeekAsync()).Tasks); // дело одно, не копия
    }

    [Fact]
    public async Task Link_ReplacesOpenSessionSameDay_RefusesWhenDone()
    {
        var steps = await GoalAsync("Тело", "10к шагов");
        var own = (await PostTargetAsync<GoalResultDto>($"/api/goals/{steps.Id}/schedule", new DayTargetDto(Monday, 0))).Task!;
        var walk = await CreateInDayAsync("Прогулка в парке");

        var result = await SetGoalAsync(walk.Id, steps.Id);

        Assert.Equal([own.Id], result.ReplacedTaskIds);
        Assert.Equal(1, (await GetBoardAsync()).TrashCount);
        Assert.Equal([walk.Id], (await ReloadAsync(steps)).Sessions.Select(s => s.TaskId));

        // Выполненное занятие в тот же день — не заменяется, дело не привязывается
        var run = await CreateInDayAsync("Бег", Monday.AddDays(1));
        var tuesday = (await PostTargetAsync<GoalResultDto>($"/api/goals/{steps.Id}/schedule", new DayTargetDto(Monday.AddDays(1), 0))).Task!;
        await PutAsync<TaskDto>($"/api/tasks/{tuesday.Id}/done", new SetDoneRequest(true));
        var refused = await SetGoalRawAsync(run.Id, steps.Id);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Null((await GetWeekAsync()).Tasks.Single(t => t.Id == run.Id).GoalId);
    }

    [Fact]
    public async Task Unlink_KeepsRolesAndDay()
    {
        var steps = await GoalAsync("Тело", "10к шагов");
        var walk = await CreateInDayAsync("Прогулка в парке");
        await SetGoalAsync(walk.Id, steps.Id);

        var result = await SetGoalAsync(walk.Id, null);

        Assert.Null(result.Task.GoalId);
        Assert.Equal(steps.RoleIds, result.Task.RoleIds);
        Assert.Equal(Monday, result.Task.PlannedDate);
        Assert.Empty((await ReloadAsync(steps)).Sessions);
    }

    [Fact]
    public async Task SomedayTask_Linked_BecomesSessionWithoutDay_UnlinkReturnsToFeed()
    {
        var steps = await GoalAsync("Тело", "10к шагов");
        var thought = await CreateTaskAsync("Поход в лес", new SomedayTargetDto(null, 0));

        var linked = await SetGoalAsync(thought.Id, steps.Id);
        Assert.Equal(TaskStateDto.Planned, linked.Task.State);
        Assert.Null(linked.Task.PlannedDate);
        Assert.Empty((await GetBoardAsync()).SomedayTasks);

        var back = await SetGoalAsync(thought.Id, null);
        Assert.Equal(TaskStateDto.Someday, back.Task.State);
    }

    [Fact]
    public async Task DaysSeries_LinkedTaskOnChosenDay_NoExtraSession()
    {
        var body = await RoleAsync("Тело");
        var steps = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(body.Id, Monday, "10к шагов"));
        var walk = await CreateInDayAsync("Прогулка в парке");
        await SetGoalAsync(walk.Id, steps.Id);

        await PutAsync<GoalUpdateResultDto>($"/api/goals/{steps.Id}",
            new UpdateGoalRequest("10к шагов", [body.Id], 1, null, null, null, null, Days: 1 | 2)); // Пн, Вт

        var sessions = (await ReloadAsync(steps)).Sessions;
        Assert.Equal(2, sessions.Count); // в Пн — прогулка, во Вт — новое занятие
        Assert.Contains(sessions, s => s.TaskId == walk.Id);
    }

    [Fact]
    public async Task GoalOfOtherWeek_Refused()
    {
        var steps = await GoalAsync("Тело", "10к шагов");
        var later = await CreateInDayAsync("Прогулка", Monday.AddDays(8));

        var response = await SetGoalRawAsync(later.Id, steps.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
