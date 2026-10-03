using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Дни у постоянных целей: занятия сами встают в выбранные дни; смена дней — с этой недели и дальше.</summary>
public class GoalDaysApiTests : ApiTestBase
{
    // Битовая маска дней, как в WeeklyGoalDto.Days
    private const int Mon = 1, Tue = 2, Wed = 4, Thu = 8, Fri = 16, EveryDay = 127;

    private static DateOnly Week(int offset) => Monday.AddDays(7 * offset);

    private async Task<WeeklyGoalDto> RecurringGoalAsync(string title = "Скакалка")
    {
        var body = (await GetBoardAsync()).Roles.First(r => r.IsSharpenSaw);
        return await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(body.Id, Monday, title));
    }

    private Task<GoalUpdateResultDto> SetDaysAsync(WeeklyGoalDto g, int days, int times = 1, TimeOnly? time = null, int? duration = null) =>
        PutAsync<GoalUpdateResultDto>($"/api/goals/{g.Id}",
            new UpdateGoalRequest(g.Title, g.RoleId, times, time, g.Quadrant, g.Notes, duration, Days: days));

    private async Task<WeeklyGoalDto> GoalOnAsync(int week, int goalId) =>
        (await GetWeekAsync(Week(week))).Goals.Single(g => g.Id == goalId || week != 0);

    private async Task<List<DateOnly>> SessionDaysAsync(int week, string title) =>
        (await GetWeekAsync(Week(week))).Tasks.Where(t => t.Title == title && t.State != TaskStateDto.Trashed)
            .Select(t => t.PlannedDate!.Value).Order().ToList();

    [Fact]
    public async Task ChosenDays_SetTimesPerWeek_AndPlaceSessionsThisWeek()
    {
        var goal = await RecurringGoalAsync();

        var result = await SetDaysAsync(goal, Mon | Wed | Fri, times: 1, time: new TimeOnly(7, 0), duration: 15);

        Assert.Equal(Mon | Wed | Fri, result.Goal.Days);
        Assert.Equal(3, result.Goal.TimesPerWeek); // «раз в неделю» = числу дней, присланное 1 не важно
        Assert.Equal([Monday, Monday.AddDays(2), Monday.AddDays(4)], await SessionDaysAsync(0, "Скакалка"));
        var session = (await GetWeekAsync()).Tasks.First(t => t.Title == "Скакалка");
        Assert.Equal((new TimeOnly(7, 0), 15), (session.StartTime!.Value, session.DurationMinutes!.Value));
        Assert.Empty(result.RemovedSessionIds!);
    }

    [Fact]
    public async Task NewWeek_GetsSessionsOnChosenDays_WithTemplate()
    {
        var goal = await RecurringGoalAsync();
        await SetDaysAsync(goal, EveryDay, time: new TimeOnly(21, 0), duration: 10);

        var next = (await GetWeekAsync(Week(1))).Goals.Single(g => g.Title == "Скакалка");

        Assert.Equal((7, EveryDay), (next.TimesPerWeek, next.Days!.Value));
        Assert.Equal(7, next.Sessions.Count);
        var days = await SessionDaysAsync(1, "Скакалка");
        Assert.Equal(Enumerable.Range(0, 7).Select(i => Week(1).AddDays(i)), days);
        Assert.All((await GetWeekAsync(Week(1))).Tasks.Where(t => t.Title == "Скакалка"),
            t => Assert.Equal((new TimeOnly(21, 0), 10, next.Id), (t.StartTime!.Value, t.DurationMinutes!.Value, t.PrimaryGoalId!.Value)));
    }

    [Fact]
    public async Task ChangeDays_AddsMissing_TrashesExtraOpen_KeepsDone_AlsoOnLaterWeeks()
    {
        var goal = await RecurringGoalAsync();
        await SetDaysAsync(goal, Mon | Wed | Fri);
        await GetWeekAsync(Week(1)); // следующую неделю уже открывали — там занятия Пн, Ср, Пт
        var monday = (await GetWeekAsync()).Tasks.Single(t => t.Title == "Скакалка" && t.PlannedDate == Monday);
        await PutAsync<TaskDto>($"/api/tasks/{monday.Id}/done", new SetDoneRequest(true));

        var result = await SetDaysAsync(await GoalOnAsync(0, goal.Id), Tue | Thu);

        // Эта неделя: выполненный Пн остался, Ср и Пт — в корзину, Вт и Чт — добавлены
        Assert.Equal([Monday, Monday.AddDays(1), Monday.AddDays(3)], await SessionDaysAsync(0, "Скакалка"));
        // Следующая: Пн, Ср, Пт — в корзину, Вт и Чт — добавлены
        Assert.Equal([Week(1).AddDays(1), Week(1).AddDays(3)], await SessionDaysAsync(1, "Скакалка"));
        Assert.Equal(5, result.RemovedSessionIds!.Count);
        Assert.Equal(5, (await GetBoardAsync()).TrashCount);

        // Через две недели — по новому правилу
        Assert.Equal([Week(2).AddDays(1), Week(2).AddDays(3)], await SessionDaysAsync(2, "Скакалка"));
    }

    [Fact]
    public async Task SessionMovedByHand_StaysRock_SeriesDaysUnchanged()
    {
        var goal = await RecurringGoalAsync();
        await SetDaysAsync(goal, Mon | Wed);
        var wednesday = (await GetWeekAsync()).Tasks.Single(t => t.Title == "Скакалка" && t.PlannedDate == Monday.AddDays(2));

        var moved = await MoveTaskAsync(wednesday.Id, new DayTargetDto(Monday.AddDays(3), 0));

        Assert.Equal(goal.Id, moved.PrimaryGoalId);
        Assert.Equal(Mon | Wed, (await GetWeekAsync()).Goals.Single(g => g.Id == goal.Id).Days);
        Assert.Equal([Week(1), Week(1).AddDays(2)], await SessionDaysAsync(1, "Скакалка"));
    }

    [Fact]
    public async Task BackToManual_TimesEditableAgain_NoSessionsTouched()
    {
        var goal = await RecurringGoalAsync();
        await SetDaysAsync(goal, Mon | Tue);

        var manual = await SetDaysAsync(await GoalOnAsync(0, goal.Id), 0, times: 5);

        Assert.Null(manual.Goal.Days);
        Assert.Equal(5, manual.Goal.TimesPerWeek);
        Assert.Equal(2, (await SessionDaysAsync(0, "Скакалка")).Count);
        Assert.Empty((await GetWeekAsync(Week(1))).Goals.Single().Sessions); // дальше — сама раскладываю
    }

    [Fact]
    public async Task UpdateWithoutDays_KeepsDays()
    {
        var goal = await RecurringGoalAsync();
        await SetDaysAsync(goal, Mon | Tue);

        var g = await GoalOnAsync(0, goal.Id);
        var result = await PutAsync<GoalUpdateResultDto>($"/api/goals/{g.Id}",
            new UpdateGoalRequest("Скакалка 100", g.RoleId, g.TimesPerWeek, null, null, null, null));

        Assert.Equal(Mon | Tue, result.Goal.Days);
        Assert.Equal(2, (await SessionDaysAsync(0, "Скакалка 100")).Count);
    }
}
