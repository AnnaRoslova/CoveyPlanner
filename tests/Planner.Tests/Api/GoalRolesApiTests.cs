using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Несколько ролей у цели: одна и та же цель видна под каждой ролью, прогресс общий.</summary>
public class GoalRolesApiTests : ApiTestBase
{
    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private Task<WeeklyGoalDto> CreateGoalAsync(int roleId, string title, bool recurring = false) =>
        PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Monday, title, recurring));

    private Task<GoalUpdateResultDto> SetRolesAsync(WeeklyGoalDto goal, params int[] roleIds) =>
        PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}", new UpdateGoalRequest(goal.Title, roleIds, goal.TimesPerWeek,
            goal.StartTime, goal.Quadrant, goal.Notes, goal.DurationMinutes));

    private Task<WeeklyGoalDto> MoveAsync(WeeklyGoalDto goal, int roleId, int index, int? fromRoleId, bool addRole = false) =>
        PostAsync<WeeklyGoalDto>($"/api/goals/{goal.Id}/move", new MoveGoalRequest(roleId, index, fromRoleId, addRole));

    [Fact]
    public async Task TwoRoles_OneGoal_SessionsGetBothRoles_NewRoleAtEnd()
    {
        var (body, family) = (await RoleIdAsync("Тело"), await RoleIdAsync("Семья"));
        await CreateGoalAsync(family, "Ужин вместе");
        var walk = await CreateGoalAsync(body, "Прогулка с детьми");
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{walk.Id}/schedule", new DayTargetDto(Monday, 0))).Task!;

        var result = await SetRolesAsync(walk, body, family);

        Assert.Equal([body, family], result.Goal.RoleIds.Order());
        Assert.Equal(1, result.Goal.SortOrderIn(family)); // в «Семье» — после «Ужина»
        var week = await GetWeekAsync();
        Assert.Equal(2, week.Goals.Count); // цель одна, хоть и в двух ролях
        Assert.Equal([body, family], week.Tasks.Single(t => t.Id == session.Id).RoleIds.Order());
    }

    [Fact]
    public async Task NoRoles_Refused()
    {
        var goal = await CreateGoalAsync(await RoleIdAsync("Тело"), "Бег");

        var response = await Client.PutAsJsonAsync($"/api/goals/{goal.Id}",
            new UpdateGoalRequest("Бег", [], 1, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Move_ReplacesSourceRole_ShiftAddsRole_KeepsOthers()
    {
        var (body, mind, work) = (await RoleIdAsync("Тело"), await RoleIdAsync("Ум"), await RoleIdAsync("Работа"));
        var goal = await CreateGoalAsync(body, "Курс по анатомии");
        await SetRolesAsync(goal, body, mind);

        // Из «Тела» в «Работу» — «Тело» заменяется, «Ум» остаётся
        var moved = await MoveAsync(goal, work, 0, fromRoleId: body);
        Assert.Equal([mind, work], moved.RoleIds.Order());

        // С Shift — ещё и «Тело»
        var added = await MoveAsync(moved, body, 0, fromRoleId: work, addRole: true);
        Assert.Equal([body, mind, work], added.RoleIds.Order());

        // В той же роли — только порядок
        var other = await CreateGoalAsync(body, "Бег");
        var reordered = await MoveAsync(other, body, 0, fromRoleId: body);
        Assert.Equal([body], reordered.RoleIds);
        Assert.Equal(0, reordered.SortOrderIn(body));
        Assert.Equal(1, (await GetWeekAsync()).Goals.Single(g => g.Id == goal.Id).SortOrderIn(body));
    }

    [Fact]
    public async Task Recurring_RolesSpreadToLaterWeeks_PastUntouched_NewWeeksTakeThem()
    {
        var (body, mind) = (await RoleIdAsync("Тело"), await RoleIdAsync("Ум"));
        var past = await CreateGoalAsync(body, "Йога", recurring: true);
        var thisWeek = (await GetWeekAsync(Monday.AddDays(7))).Goals.Single(g => g.Title == "Йога");
        await GetWeekAsync(Monday.AddDays(14)); // следующая неделя уже создана

        await SetRolesAsync(thisWeek, body, mind);

        Assert.Equal([body], (await GetWeekAsync()).Goals.Single(g => g.Id == past.Id).RoleIds);
        Assert.Equal([body, mind], (await GetWeekAsync(Monday.AddDays(14))).Goals.Single(g => g.Title == "Йога").RoleIds.Order());
        Assert.Equal([body, mind], (await GetWeekAsync(Monday.AddDays(21))).Goals.Single(g => g.Title == "Йога").RoleIds.Order());
    }

    [Fact]
    public async Task DeleteRole_GoalWithOtherRolesStays_SingleRoleGoalGoes()
    {
        var body = await RoleIdAsync("Тело");
        var work = await RoleIdAsync("Работа");
        var shared = await CreateGoalAsync(work, "Пешком до офиса", recurring: true);
        await SetRolesAsync(shared, body, work);
        await CreateGoalAsync(work, "Отчёт");

        (await Client.DeleteAsync($"/api/roles/{work}")).EnsureSuccessStatusCode();

        var week = await GetWeekAsync();
        Assert.Equal(["Пешком до офиса"], week.Goals.Select(g => g.Title));
        Assert.Equal([body], week.Goals.Single().RoleIds);
        Assert.Equal([body], (await GetWeekAsync(Monday.AddDays(7))).Goals.Single().RoleIds); // и в серии
    }
}
