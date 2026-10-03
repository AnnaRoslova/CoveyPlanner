using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Постоянные цели («каждую неделю»): появляются на следующих неделях, правка и удаление — с этой недели.</summary>
public class GoalSeriesApiTests : ApiTestBase
{
    private static DateOnly Week(int offset) => Monday.AddDays(7 * offset);

    private async Task<RoleDto> RoleAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name);

    private Task<WeeklyGoalDto> CreateGoalAsync(int roleId, string title, int week = 0, bool? recurring = null) =>
        PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Week(week), title, recurring));

    private async Task<List<string>> TitlesAsync(int week, int roleId) =>
        (await GetWeekAsync(Week(week))).Goals.Where(g => g.HasRole(roleId)).OrderBy(g => g.SortOrderIn(roleId)).Select(g => g.Title).ToList();

    private async Task<WeeklyGoalDto> GoalAsync(int week, string title) =>
        (await GetWeekAsync(Week(week))).Goals.Single(g => g.Title == title);

    private static UpdateGoalRequest FormOf(WeeklyGoalDto g) =>
        new(g.Title, g.RoleIds, g.TimesPerWeek, g.StartTime, g.Quadrant, g.Notes, g.DurationMinutes);

    private Task<GoalUpdateResultDto> UpdateAsync(WeeklyGoalDto goal, Func<UpdateGoalRequest, UpdateGoalRequest> change) =>
        PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}", change(FormOf(goal)));

    private async Task DeleteAsync(WeeklyGoalDto goal, bool series) =>
        (await Client.DeleteAsync($"/api/goals/{goal.Id}{(series ? "?scope=series" : "")}")).EnsureSuccessStatusCode();

    [Fact]
    public async Task NewGoal_RecurringByDefault_OnlyForSharpenSaw()
    {
        var body = await RoleAsync("Тело");
        var work = await RoleAsync("Работа");

        Assert.True((await CreateGoalAsync(body.Id, "Бассейн")).IsRecurring);
        Assert.False((await CreateGoalAsync(work.Id, "Отчёт")).IsRecurring);
        Assert.True((await CreateGoalAsync(work.Id, "Планёрка", recurring: true)).IsRecurring);
        Assert.False((await CreateGoalAsync(body.Id, "Марафон", recurring: false)).IsRecurring);
    }

    [Fact]
    public async Task Recurring_AppearsOnNextWeeks_WithTemplate_NoSessions_NoDuplicates()
    {
        var work = await RoleAsync("Работа");
        var goal = await CreateGoalAsync(work.Id, "Планёрка", recurring: true);
        await UpdateAsync(goal, f => f with { TimesPerWeek = 2, StartTime = new TimeOnly(9, 30), DurationMinutes = 30, Notes = "Zoom", Quadrant = 2 });
        await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(Monday, 0));

        // Через неделю открыли сразу третью: пропущенная тоже заполнилась
        var third = await GoalAsync(2, "Планёрка");
        var second = await GoalAsync(1, "Планёрка");
        foreach (var later in new[] { second, third })
        {
            Assert.Equal((2, new TimeOnly(9, 30), 30, "Zoom", 2, work.Id), (later.TimesPerWeek, later.StartTime!.Value, later.DurationMinutes!.Value, later.Notes!, later.Quadrant!.Value, later.RoleIds.Single()));
            Assert.Empty(later.Sessions);
            Assert.True(later.IsRecurring);
        }

        await GetWeekAsync(Week(1));
        Assert.Equal(["Планёрка"], await TitlesAsync(1, work.Id));
        Assert.Empty((await GetWeekAsync(Week(-1))).Goals); // назад серия не идёт
    }

    [Fact]
    public async Task PoolThenTennis_Scenario()
    {
        var body = await RoleAsync("Тело");

        // Месяц «Бассейн» каждую неделю
        await CreateGoalAsync(body.Id, "Бассейн", week: -4);
        for (var w = -3; w <= 0; w++)
        {
            Assert.Equal(["Бассейн"], await TitlesAsync(w, body.Id));
        }

        // На этой неделе: «Бассейн» — удалить с этой недели и дальше, «Теннис» — каждую неделю
        await DeleteAsync(await GoalAsync(0, "Бассейн"), series: true);
        await CreateGoalAsync(body.Id, "Теннис");

        Assert.Equal(["Теннис"], await TitlesAsync(0, body.Id));
        Assert.Equal(["Теннис"], await TitlesAsync(1, body.Id));
        Assert.Equal(["Теннис"], await TitlesAsync(2, body.Id));
        for (var w = -4; w <= -1; w++)
        {
            Assert.Equal(["Бассейн"], await TitlesAsync(w, body.Id));
        }
    }

    [Fact]
    public async Task DeleteOnlyThisWeek_ComesBackNextWeek_NotThisWeek()
    {
        var body = await RoleAsync("Тело");
        var goal = await CreateGoalAsync(body.Id, "Бассейн");

        await DeleteAsync(goal, series: false);

        Assert.Empty(await TitlesAsync(0, body.Id));
        Assert.Equal(["Бассейн"], await TitlesAsync(1, body.Id));
        Assert.Empty(await TitlesAsync(0, body.Id)); // открыли снова — не вернулась
    }

    [Fact]
    public async Task DeleteFromThisWeekOn_RemovesAlreadyCreatedLaterWeeks()
    {
        var body = await RoleAsync("Тело");
        await CreateGoalAsync(body.Id, "Бассейн");
        await GetWeekAsync(Week(2)); // будущие недели уже открывали

        await DeleteAsync(await GoalAsync(1, "Бассейн"), series: true);

        Assert.Equal(["Бассейн"], await TitlesAsync(0, body.Id));
        Assert.Empty(await TitlesAsync(1, body.Id));
        Assert.Empty(await TitlesAsync(2, body.Id));
        Assert.Empty(await TitlesAsync(5, body.Id));
        Assert.False((await GoalAsync(0, "Бассейн")).IsRecurring);
    }

    [Fact]
    public async Task TemplateChange_AppliesToThisAndLaterWeeks_NotEarlier()
    {
        var body = await RoleAsync("Тело");
        var mind = await RoleAsync("Ум");
        await CreateGoalAsync(body.Id, "Бег", week: -1);
        await GetWeekAsync(Week(2));

        await UpdateAsync(await GoalAsync(0, "Бег"), f => f with { Title = "Бег 5 км", TimesPerWeek = 3, RoleIds = [mind.Id] });

        var before = await GoalAsync(-1, "Бег");
        Assert.Equal((1, body.Id), (before.TimesPerWeek, before.RoleIds.Single()));
        foreach (var w in new[] { 0, 1, 2, 3 })
        {
            var g = await GoalAsync(w, "Бег 5 км");
            Assert.Equal((3, mind.Id), (g.TimesPerWeek, g.RoleIds.Single()));
        }
    }

    [Fact]
    public async Task TurnOff_EndsAfterThisWeek_TurnOnAgain_ContinuesFromThatWeek()
    {
        var work = await RoleAsync("Работа");
        var goal = await CreateGoalAsync(work.Id, "Планёрка", recurring: true);
        await GetWeekAsync(Week(1));

        var off = (await UpdateAsync(goal, f => f with { IsRecurring = false })).Goal;
        Assert.False(off.IsRecurring);
        Assert.Empty(await TitlesAsync(1, work.Id));
        Assert.Empty(await TitlesAsync(3, work.Id));

        // Включили снова на третьей неделе (там цель создали руками) — дальше снова каждую неделю
        var again = await CreateGoalAsync(work.Id, "Планёрка", week: 3, recurring: false);
        Assert.True((await UpdateAsync(again, f => f with { IsRecurring = true })).Goal.IsRecurring);
        Assert.Equal(["Планёрка"], await TitlesAsync(4, work.Id));
        Assert.Empty(await TitlesAsync(2, work.Id));

        // Правка без поля IsRecurring (старые клиенты) «каждую неделю» не выключает
        Assert.True((await UpdateAsync(await GoalAsync(4, "Планёрка"), f => f with { Notes = "x" })).Goal.IsRecurring);
    }

    [Fact]
    public async Task DeleteRole_RemovesItsSeries()
    {
        var role = await PostAsync<RoleDto>("/api/roles", new CreateRoleRequest("Спорт", null));
        await CreateGoalAsync(role.Id, "Йога", recurring: true);

        (await Client.DeleteAsync($"/api/roles/{role.Id}")).EnsureSuccessStatusCode();

        Assert.Empty((await GetWeekAsync(Week(1))).Goals);
    }
}
