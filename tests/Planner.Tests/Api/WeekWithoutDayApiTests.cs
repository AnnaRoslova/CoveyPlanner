using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// «На неделе, без дня» (docs/specs/next-batch.md, N4, вариант Б): дело запланировано на неделю, день не выбран (Tasks.PlannedWeek).
/// Камнем не становится; видно в неделе (клиент — в левой панели под ролью); дыра «дело из дня → цель другой недели» закрыта.
/// </summary>
public class WeekWithoutDayApiTests : ApiTestBase
{
    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    private Task<TaskDto> ToWeekAsync(int id, DateOnly week, int? roleId = null) =>
        MoveTaskAsync(id, new WeekTargetDto(week, roleId));

    private async Task<TaskDto?> InWeekAsync(int id, DateOnly week) => (await GetWeekAsync(week)).Tasks.SingleOrDefault(t => t.Id == id);

    [Fact]
    public async Task N4_1_Thought_LeavesFeed_ShownInNextWeek_WithoutDay()
    {
        var thought = await CreateTaskAsync("Купить подарок", new SomedayTargetDto(null, 0));

        var moved = await ToWeekAsync(thought.Id, NextMonday.AddDays(3)); // любой день недели — к понедельнику

        Assert.Equal((TaskStateDto.Planned, null, NextMonday), (moved.State, moved.PlannedDate, moved.PlannedWeek));
        Assert.DoesNotContain((await GetBoardAsync()).SomedayTasks, t => t.Id == thought.Id);
        Assert.NotNull(await InWeekAsync(thought.Id, NextMonday));
        Assert.Null(await InWeekAsync(thought.Id, Monday));
    }

    [Fact]
    public async Task N4_1_DayTask_LosesDayAndTime_BackToDay_ClearsWeek()
    {
        var call = await CreateInDayAsync("Звонок");
        call = await UpdateTaskAsync(call, startTime: new TimeOnly(15, 0));

        var moved = await ToWeekAsync(call.Id, NextMonday);
        Assert.Equal((null, null, NextMonday), (moved.PlannedDate, moved.StartTime, moved.PlannedWeek));

        var back = await MoveTaskAsync(call.Id, new DayTargetDto(NextMonday.AddDays(2), 0));
        Assert.Equal((NextMonday.AddDays(2), null), (back.PlannedDate, back.PlannedWeek));
    }

    [Fact]
    public async Task N4_5_Session_CannotGoToWeekWithoutDay()
    {
        var roleId = (await GetBoardAsync()).Roles.First().Id;
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Monday, "Отчёт"));
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(Monday, 0))).Task!;

        var response = await Client.PostAsJsonAsync<TaskTargetDto>($"/api/tasks/{session.Id}/move", new WeekTargetDto(NextMonday));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task N4_Done_ThenUndone_StaysInWeek_NotFeed()
    {
        var thought = await CreateTaskAsync("Подарок", new SomedayTargetDto(null, 0));
        await ToWeekAsync(thought.Id, NextMonday);

        await SetTaskDoneAsync(thought.Id, true);
        var undone = await SetTaskDoneAsync(thought.Id, false);

        Assert.Equal((TaskStateDto.Planned, NextMonday), (undone.State, undone.PlannedWeek));
    }

    [Fact]
    public async Task N4_3_DropOnRoleWeekRow_AddsRole()
    {
        var roleId = (await GetBoardAsync()).Roles.First(r => r.Name == "Семья").Id;
        var thought = await CreateTaskAsync("Поход", new SomedayTargetDto(null, 0));

        var moved = await ToWeekAsync(thought.Id, Monday, roleId);

        Assert.Contains(roleId, moved.RoleIds);
    }

    [Fact]
    public async Task N4_Hole_DayTaskBecomesGoalOfAnotherWeek_LosesDay()
    {
        var roleId = (await GetBoardAsync()).Roles.First(r => r.Name == "Работа").Id;
        var call = await CreateInDayAsync("Звонок", Monday);

        await PostAsync<GoalResultDto>("/api/goals/from-task", new GoalFromTaskRequest(call.Id, roleId, NextMonday, 0));

        var session = (await GetWeekAsync(NextMonday)).Tasks.Single(t => t.Id == call.Id);
        Assert.Null(session.PlannedDate);
        Assert.DoesNotContain((await GetWeekAsync(Monday)).Tasks, t => t.Id == call.Id && t.PlannedDate == Monday); // в своём дне его больше нет
    }

    [Fact]
    public async Task N4_5_StepToWeek_StaysStep_RemovedFromProject_StaysInWeek()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var step = await PostAsync<StepDto>($"/api/tasks/{repair.Id}/steps", new StepRequest("Обои"));

        var moved = await ToWeekAsync(step.Id, NextMonday);
        Assert.Equal((repair.Id, NextMonday), (moved.ParentId!.Value, moved.PlannedWeek));

        var free = await PutAsync<TaskDto>($"/api/tasks/{step.Id}/parent", new SetParentRequest(null));
        Assert.Equal((TaskStateDto.Planned, NextMonday), (free.State, free.PlannedWeek));
    }

    [Fact]
    public async Task N4_TrashAndRestore_BackInWeek()
    {
        var thought = await CreateTaskAsync("Подарок", new SomedayTargetDto(null, 0));
        await ToWeekAsync(thought.Id, NextMonday);

        (await Client.DeleteAsync($"/api/tasks/{thought.Id}")).EnsureSuccessStatusCode();
        var restored = await PostAsync<TaskDto>($"/api/trash/{thought.Id}/restore", new { });

        Assert.Equal((TaskStateDto.Planned, NextMonday), (restored.State, restored.PlannedWeek));
        Assert.NotNull(await InWeekAsync(thought.Id, NextMonday));
    }
}
