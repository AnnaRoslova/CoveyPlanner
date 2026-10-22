using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planner.Server.Data;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Мини-календарь «перейти к дате» (batch-3.md, J3, J7): точки на днях с делами, только чтение.</summary>
public class CalendarApiTests : ApiTestBase
{
    private Task<CalendarDto> CalendarAsync(DateOnly from, DateOnly to) =>
        GetAsync<CalendarDto>($"/api/calendar?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    [Fact]
    public async Task BusyDays_PlannedAndDone_NotThoughtsOrTrash()
    {
        await CreateInDayAsync("Созвон", Monday);
        await CreateInDayAsync("Ещё в понедельник", Monday);
        var done = await CreateInDayAsync("Бег", Monday.AddDays(2));
        await SetTaskDoneAsync(done.Id, true);
        var trashed = await CreateInDayAsync("Передумала", Monday.AddDays(4));
        (await Client.DeleteAsync($"/api/tasks/{trashed.Id}")).EnsureSuccessStatusCode();
        await CreateTaskAsync("Мысль", new SomedayTargetDto(null, 0));
        await CreateInDayAsync("Другой месяц", Monday.AddDays(60));

        var calendar = await CalendarAsync(Monday, Monday.AddDays(41));

        Assert.Equal([Monday, Monday.AddDays(2)], calendar.BusyDays);
    }

    [Fact]
    public async Task ReadOnly_DoesNotCreateRecurringGoals()
    {
        var body = (await GetBoardAsync()).Roles.Single(r => r.Name == "Тело");
        await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(body.Id, Monday, "Бассейн", true));
        var before = await CountGoalsAsync();

        await CalendarAsync(Monday.AddDays(7), Monday.AddDays(7 * 20));

        Assert.Equal(before, await CountGoalsAsync());
    }

    [Theory]
    [InlineData("2026-10-31", "2026-10-01")] // конец раньше начала
    [InlineData("2026-01-01", "2027-01-02")] // больше года
    public async Task BadPeriod_400(string from, string to)
    {
        var response = await Client.GetAsync($"/api/calendar?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<int> CountGoalsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().WeeklyGoals.CountAsync();
    }
}
