using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Перенос дела на другую неделю; занятие цели («камень») остаётся камнем — в цели той же серии.</summary>
public class SessionTransferApiTests : ApiTestBase
{
    private static DateOnly Week(int offset) => Monday.AddDays(7 * offset);

    private async Task<RoleDto> RoleAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name);

    private Task<WeeklyGoalDto> CreateGoalAsync(int roleId, string title, int week = 0, bool? recurring = null) =>
        PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Week(week), title, recurring));

    private async Task<TaskDto> ScheduleAsync(int goalId, DateOnly date) =>
        (await PostTargetAsync<GoalResultDto>($"/api/goals/{goalId}/schedule", new DayTargetDto(date, 0))).Task!;

    private async Task<List<WeeklyGoalDto>> GoalsAsync(int week, string title) =>
        (await GetWeekAsync(Week(week))).Goals.Where(g => g.Title == title).ToList();

    [Fact]
    public async Task PlainTask_MovesToAnyDate_KeepsTime()
    {
        var task = await CreateInDayAsync("Стоматолог");
        task = await UpdateTaskAsync(task, startTime: new TimeOnly(15, 0));

        var moved = await MoveTaskAsync(task.Id, new DayTargetDto(Week(2).AddDays(3), int.MaxValue));

        Assert.Equal(Week(2).AddDays(3), moved.PlannedDate);
        Assert.Equal(new TimeOnly(15, 0), moved.StartTime);
        Assert.Contains((await GetWeekAsync(Week(2))).Tasks, t => t.Id == task.Id);
        Assert.Empty((await GetWeekAsync()).Tasks);
    }

    [Fact]
    public async Task Session_OfOneOffGoal_CreatesCopyGoal_ThenSecondSessionJoinsIt()
    {
        var work = await RoleAsync("Работа");
        var goal = await CreateGoalAsync(work.Id, "Бег", recurring: false);
        await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest("Бег", [work.Id], 3, new TimeOnly(7, 30), 2, "парк", 30));
        var first = await ScheduleAsync(goal.Id, Monday);
        var second = await ScheduleAsync(goal.Id, Monday.AddDays(2));

        var moved = await MoveTaskAsync(first.Id, new DayTargetDto(Week(1).AddDays(1), 0));
        await MoveTaskAsync(second.Id, new DayTargetDto(Week(1).AddDays(4), 0));

        var copy = Assert.Single(await GoalsAsync(1, "Бег"));
        Assert.Equal(copy.Id, moved.GoalId);
        Assert.Equal((1, new TimeOnly(7, 30), 2, "парк", 30, false),
            (copy.TimesPerWeek, copy.StartTime!.Value, copy.Quadrant!.Value, copy.Notes!, copy.DurationMinutes!.Value, copy.IsRecurring));
        Assert.Equal(2, copy.Sessions.Count); // второе занятие — в ту же цель, а не в новую

        var source = Assert.Single(await GoalsAsync(0, "Бег"));
        Assert.Empty(source.Sessions); // прогресс прежней цели уменьшился
        Assert.Empty(await GoalsAsync(2, "Бег")); // без повтора
    }

    [Fact]
    public async Task Session_OfRecurringGoal_JoinsSameSeriesGoalOnThatWeek()
    {
        var body = await RoleAsync("Тело");
        var goal = await CreateGoalAsync(body.Id, "Бассейн");
        var session = await ScheduleAsync(goal.Id, Monday.AddDays(1));

        // Следующую неделю ещё не открывали — цель серии появится при переносе
        var moved = await MoveTaskAsync(session.Id, new DayTargetDto(Week(1).AddDays(2), 0));

        var next = Assert.Single(await GoalsAsync(1, "Бассейн"));
        Assert.Equal(next.Id, moved.GoalId);
        Assert.True(next.IsRecurring);
        Assert.Equal([session.Id], next.Sessions.Select(s => s.TaskId));
        Assert.Empty(Assert.Single(await GoalsAsync(0, "Бассейн")).Sessions);
    }

    [Fact]
    public async Task Session_ToWeekSkippedBySeries_CreatesOneOffGoalThere()
    {
        var body = await RoleAsync("Тело");
        var goal = await CreateGoalAsync(body.Id, "Бассейн");
        var session = await ScheduleAsync(goal.Id, Monday);
        var next = Assert.Single(await GoalsAsync(1, "Бассейн"));
        (await Client.DeleteAsync($"/api/goals/{next.Id}")).EnsureSuccessStatusCode(); // только на той неделе

        var moved = await MoveTaskAsync(session.Id, new DayTargetDto(Week(1), 0));

        var created = Assert.Single(await GoalsAsync(1, "Бассейн"));
        Assert.Equal(created.Id, moved.GoalId);
        Assert.Equal(1, created.TimesPerWeek);
    }

    [Fact]
    public async Task Session_MovedWithinItsWeek_StaysWithGoal()
    {
        var work = await RoleAsync("Работа");
        var goal = await CreateGoalAsync(work.Id, "Отчёт");
        var session = await ScheduleAsync(goal.Id, Monday);

        var moved = await MoveTaskAsync(session.Id, new DayTargetDto(Monday.AddDays(4), 0));

        Assert.Equal(goal.Id, moved.GoalId);
        Assert.Empty(await GoalsAsync(1, "Отчёт"));
    }
}
