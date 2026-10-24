using System.Text;
using Planner.Core.Time;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Пропуски в отчёте, CSV и дашборде (docs/specs/skips.md, O1–O4).</summary>
public class SkipReportApiTests : ApiTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly DateOnly ThisMonday = Week.StartOf(Today);
    private static readonly string Period = $"from={ThisMonday:yyyy-MM-dd}&to={ThisMonday.AddDays(6):yyyy-MM-dd}";

    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private async Task<WeeklyGoalDto> GoalAsync(string role, string title, int times, bool recurring)
    {
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync(role), ThisMonday, title, recurring));
        return (await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, times, null, null, null, null))).Goal;
    }

    private async Task<TaskDto> SessionAsync(WeeklyGoalDto goal, int day) =>
        (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(ThisMonday.AddDays(day), 0))).Task!;

    private Task Skip(int id, string kind, string? reason = null) =>
        PutAsync<TaskSkipResultDto>($"/api/tasks/{id}/skip", new SkipTaskRequest(kind, reason));

    /// <summary>Привычка «Бег» 3 раза: сделано, не сделала (заболела), не надо; разовая «Отчёт»: не сделала (забила);
    /// дело «Стоматолог» с ролью «Я сама»: не надо (перенесли); дело «Звонок» с ролью «Работа»: сделано.</summary>
    private async Task SeedAsync()
    {
        var run = await GoalAsync("Тело", "Бег", 3, recurring: true);
        await SetTaskDoneAsync((await SessionAsync(run, 0)).Id, true);
        await Skip((await SessionAsync(run, 1)).Id, "Missed", "заболела");
        await Skip((await SessionAsync(run, 2)).Id, "NotNeeded");

        var report = await GoalAsync("Работа", "Отчёт", 1, recurring: false);
        await Skip((await SessionAsync(report, 3)).Id, "Missed", "Забила");

        var dentist = await CreateInDayAsync("Стоматолог", ThisMonday.AddDays(4));
        await UpdateTaskAsync(dentist, roleIds: [await RoleIdAsync("Я сама")]);
        await Skip(dentist.Id, "NotNeeded", "перенесли");

        var call = await CreateInDayAsync("Звонок", ThisMonday.AddDays(4));
        await UpdateTaskAsync(call, roleIds: [await RoleIdAsync("Работа")]);
        await SetTaskDoneAsync(call.Id, true);
    }

    [Fact]
    public async Task Report_SeriesOneOffRolesReasons()
    {
        await SeedAsync();
        var report = await GetAsync<ReportDto>($"/api/report?{Period}");

        var run = report.Series.Single(s => s.Title == "Бег");
        Assert.Equal((2, 1, 1, 1), (run.Plan, run.Fact, run.Missed, run.NotNeeded)); // план 3 − «не надо»
        Assert.Equal((1, 0, 1, 0), (report.OneOff.Goals, report.OneOff.Done, report.OneOff.Missed, report.OneOff.NotNeeded));

        var roles = report.Roles!.ToDictionary(r => r.Name);
        Assert.Equal((1, 1, 1), (roles["Тело"].Done, roles["Тело"].Missed, roles["Тело"].NotNeeded));
        Assert.Equal((1, 1, 0), (roles["Работа"].Done, roles["Работа"].Missed, roles["Работа"].NotNeeded));
        Assert.Equal((0, 0, 1), (roles["Я сама"].Done, roles["Я сама"].Missed, roles["Я сама"].NotNeeded));

        Assert.Equal(4, report.Skips!.Count);
        Assert.Equal(report.Skips.OrderByDescending(s => s.Date).Select(s => s.Date), report.Skips.Select(s => s.Date)); // свежие сверху
        var sick = report.Skips.Single(s => s.Reason == "заболела");
        Assert.Equal(("Бег", "Missed", "Бег"), (sick.Title, sick.Kind, sick.Goal));
        Assert.Contains("Тело", sick.Roles);
        Assert.Null(report.Skips.Single(s => s.Title == "Стоматолог").Goal);
    }

    [Fact]
    public async Task Csv_HasSkipColumns_Roles_AndReasons()
    {
        await SeedAsync();
        var bytes = await Client.GetByteArrayAsync($"/api/report.csv?{Period}");
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Раздел;Название;Роль/категория;План;Факт;%;Не сделала;Не надо", csv);
        Assert.Contains("Привычка;Бег;Тело;2;1;50;1;1", csv);
        Assert.Contains("Роль;Я сама;0;0;1", csv);
        Assert.Contains($"Пропуск;{ThisMonday.AddDays(1):dd.MM.yyyy};Бег;не сделала;заболела;Тело;Бег", csv);
        Assert.Contains("Пропуск;" + ThisMonday.AddDays(4).ToString("dd.MM.yyyy") + ";Стоматолог;не надо;перенесли;Я сама;", csv);
    }

    [Fact]
    public async Task Dashboard_SkipCard_CountsAndTopReasons()
    {
        await SeedAsync();
        var dashboard = await GetAsync<DashboardDto>($"/api/dashboard?{Period}");

        Assert.Equal((2, 2), (dashboard.Skips!.Missed, dashboard.Skips.NotNeeded));
        Assert.Equal(["забила", "заболела", "перенесли"], dashboard.Skips.TopReasons.Select(r => r.Reason).Order());
    }
}
