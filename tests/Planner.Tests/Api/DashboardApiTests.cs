using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Дашборд «Отчёта» (docs/specs/batch-ux.md, BD8): скрепки по дням и сериям, занятия по измерениям пилы, выполненное по ролям
/// (пила сверху, «без роли» внизу), II квадрант по неделям, давно лежащие мысли.
/// </summary>
public class DashboardApiTests : ApiTestBase
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct11 = new(2026, 10, 11);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static DateOnly WeekOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private Task<DashboardDto> DashboardAsync(DateOnly from, DateOnly to) =>
        GetAsync<DashboardDto>($"/api/dashboard?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    private async Task DoneSessionAsync(WeeklyGoalDto goal, DateOnly day)
    {
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(day, 0))).Task!;
        await SetTaskDoneAsync(session.Id, true);
    }

    [Fact]
    public async Task BD2_HabitClipsByDay_InWeeksOfPeriod_WithStreaks()
    {
        var run = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync("Тело"), Monday, "Бег", true));
        await DoneSessionAsync(run, Monday.AddDays(1)); // 29 сен — неделя 28 сен входит в октябрь по четвергу

        var dashboard = await DashboardAsync(Oct1, Oct11);

        Assert.Equal([Monday, Monday.AddDays(7)], dashboard.Weeks);
        var habit = dashboard.Habits.Single(h => h.Title == "Бег");
        Assert.Equal([Monday.AddDays(1)], habit.Clips);
        Assert.True(habit.BestWeekStreak >= habit.WeekStreak);
    }

    [Fact]
    public async Task BD3_BD4_BD5_DoneByCompletionDay_SawFirst_NoRoleLast_SecondQuadrantAmongMarked()
    {
        var body = await RoleIdAsync("Тело");
        var work = await RoleIdAsync("Работа");
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(body, WeekOf(Today), "Зарядка", false));
        await DoneSessionAsync(goal, Today);
        var report = await CreateInDayAsync("Отчёт", Today);
        await PutAsync<TaskDto>($"/api/tasks/{report.Id}", new UpdateTaskRequest(report.Title, null, [work], 2, null, null));
        await SetTaskDoneAsync(report.Id, true);
        var call = await CreateInDayAsync("Звонок", Today);
        await PutAsync<TaskDto>($"/api/tasks/{call.Id}", new UpdateTaskRequest(call.Title, null, [], 1, null, null));
        await SetTaskDoneAsync(call.Id, true);
        await CreateInDayAsync("Не сделано", Today);

        var dashboard = await DashboardAsync(Today, Today);

        Assert.Equal(1, dashboard.Saw.Single(s => s.Name == "Тело").Count);
        Assert.Equal(0, dashboard.Saw.Single(s => s.Name == "Ум").Count);
        Assert.Equal(
            [("Тело", 1, true), ("Работа", 1, false), ("без роли", 1, false)],
            dashboard.Roles.Select(r => (r.Name, r.Count, r.IsSaw)).ToList());
        // II квадрант — по неделям периода (неделя входит по четвергу), поэтому период — неделя целиком
        var weekly = (await DashboardAsync(WeekOf(Today), WeekOf(Today).AddDays(6))).Quadrants.Single();
        Assert.Equal((1, 2, 1), (weekly.Second, weekly.Marked, weekly.Unmarked));
        Assert.Empty((await DashboardAsync(Today.AddDays(-30), Today.AddDays(-1))).Roles);
    }

    [Fact]
    public async Task BD6_FreshThoughts_AreNotStale()
    {
        await CreateTaskAsync("Сад на балконе", new SomedayTargetDto(null, 0));

        var stale = (await DashboardAsync(Oct1, Oct11)).Stale;

        Assert.Equal((3, 0), (stale.Months, stale.Count));
        Assert.Empty(stale.Oldest);
    }
}
