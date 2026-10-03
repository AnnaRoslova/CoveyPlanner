using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Не больше одного занятия цели в день — если у цели не включено «можно несколько раз в день».</summary>
public class OnePerDayApiTests : ApiTestBase
{
    private async Task<WeeklyGoalDto> GoalAsync(string title = "Чтение", bool several = false)
    {
        var role = (await GetBoardAsync()).Roles.First(r => !r.IsSharpenSaw);
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(role.Id, Monday, title));
        return (await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(title, [role.Id], 3, null, null, null, null, AllowSeveralPerDay: several))).Goal;
    }

    private Task<HttpResponseMessage> ScheduleAsync(int goalId, DateOnly date) =>
        Client.PostAsJsonAsync($"/api/goals/{goalId}/schedule", (TaskTargetDto)new DayTargetDto(date, 0));

    private async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemTitle>())!.Title;

    private sealed record ProblemTitle(string Title);

    [Fact]
    public async Task GoalToDay_WhereSessionExists_Refused_UnlessSeveralAllowed()
    {
        var goal = await GoalAsync();
        (await ScheduleAsync(goal.Id, Monday)).EnsureSuccessStatusCode();

        var again = await ScheduleAsync(goal.Id, Monday);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("Уже есть в этот день.", await ProblemTitleAsync(again));
        Assert.Single((await GetWeekAsync()).Tasks);

        var reading = await GoalAsync("Чтение утром и вечером", several: true);
        Assert.True(reading.AllowSeveralPerDay);
        (await ScheduleAsync(reading.Id, Monday)).EnsureSuccessStatusCode();
        (await ScheduleAsync(reading.Id, Monday)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task MoveSession_ToDayWithSession_Refused_SameDayReorderAllowed()
    {
        var goal = await GoalAsync();
        await ScheduleAsync(goal.Id, Monday);
        var tuesday = (await (await ScheduleAsync(goal.Id, Monday.AddDays(1))).Content.ReadFromJsonAsync<GoalResultDto>())!.Task!;

        var move = await Client.PostAsJsonAsync($"/api/tasks/{tuesday.Id}/move", (TaskTargetDto)new DayTargetDto(Monday, 0));
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
        Assert.Equal(Monday.AddDays(1), (await GetWeekAsync()).Tasks.Single(t => t.Id == tuesday.Id).PlannedDate);

        // Порядок внутри своего дня — можно
        await MoveTaskAsync(tuesday.Id, new DayTargetDto(Monday.AddDays(1), 5));
    }

    [Fact]
    public async Task MoveSession_ToOtherWeek_DayTakenThere_Refused()
    {
        var goal = await GoalAsync();
        var first = (await (await ScheduleAsync(goal.Id, Monday)).Content.ReadFromJsonAsync<GoalResultDto>())!.Task!;
        var second = (await (await ScheduleAsync(goal.Id, Monday.AddDays(1))).Content.ReadFromJsonAsync<GoalResultDto>())!.Task!;
        var nextMonday = Monday.AddDays(7);
        await MoveTaskAsync(first.Id, new DayTargetDto(nextMonday, 0));

        var move = await Client.PostAsJsonAsync($"/api/tasks/{second.Id}/move", (TaskTargetDto)new DayTargetDto(nextMonday, 0));

        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
        Assert.Equal(Monday.AddDays(1), (await GetWeekAsync()).Tasks.Single(t => t.Id == second.Id).PlannedDate);
    }

    [Fact]
    public async Task GoalCheckbox_TodayAlreadyDone_NoOpenSessions_Refused()
    {
        var goal = await GoalAsync();
        var today = Monday.AddDays(2);
        var session = (await (await ScheduleAsync(goal.Id, today)).Content.ReadFromJsonAsync<GoalResultDto>())!.Task!;
        await PutAsync<TaskDto>($"/api/tasks/{session.Id}/done", new SetDoneRequest(true));

        var toggle = await Client.PostAsJsonAsync($"/api/goals/{goal.Id}/toggle", new ToggleGoalRequest(today));

        Assert.Equal(HttpStatusCode.BadRequest, toggle.StatusCode);
        Assert.Equal("Сегодня уже отмечено.", await ProblemTitleAsync(toggle));
    }

    [Fact]
    public async Task SeveralPerDay_SpreadsToLaterWeeksOfRecurringGoal()
    {
        var role = (await GetBoardAsync()).Roles.First(r => r.IsSharpenSaw);
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(role.Id, Monday, "Чтение"));
        await GetWeekAsync(Monday.AddDays(7));

        await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest("Чтение", [role.Id], 2, null, null, null, null, AllowSeveralPerDay: true));

        Assert.True((await GetWeekAsync(Monday.AddDays(7))).Goals.Single().AllowSeveralPerDay);
        Assert.True((await GetWeekAsync(Monday.AddDays(14))).Goals.Single().AllowSeveralPerDay);
    }
}
