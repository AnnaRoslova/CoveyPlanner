using System.Text;
using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Отчёт за период (docs/specs/next-batch.md, N5): недели — по четвергу, план — нормы недель серии, факт — скрепки в этих неделях,
/// разовые цели одной строкой, «Выполненное» по дню отметки; CSV для Excel.
/// </summary>
public class ReportApiTests : ApiTestBase
{
    // Октябрь 2026: четверги 1, 8, 15, 22, 29 — недели с понедельниками 28 сен … 26 окт (неделя 28 сен — октябрьская)
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct11 = new(2026, 10, 11);
    private static readonly DateOnly Oct5 = new(2026, 10, 5);

    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private Task<ReportDto> ReportAsync(DateOnly from, DateOnly to) =>
        GetAsync<ReportDto>($"/api/report?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    private async Task<WeeklyGoalDto> SetTimesAsync(WeeklyGoalDto g, int times) =>
        (await PutAsync<GoalUpdateResultDto>($"/api/goals/{g.Id}",
            new UpdateGoalRequest(g.Title, g.RoleIds, times, g.StartTime, g.Quadrant, g.Notes, g.DurationMinutes))).Goal;

    private async Task DoneSessionAsync(WeeklyGoalDto goal, DateOnly day)
    {
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(day, 0))).Task!;
        await SetTaskDoneAsync(session.Id, true);
    }

    [Fact]
    public void N5_3_WeeksOf_ByThursday()
    {
        var weeks = ReportService.WeeksOf(Oct1, new DateOnly(2026, 10, 31));

        Assert.Equal(
            [new(2026, 9, 28), new(2026, 10, 5), new(2026, 10, 12), new(2026, 10, 19), new(2026, 10, 26)],
            weeks);
        Assert.Empty(ReportService.WeeksOf(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 4))); // пятница–воскресенье
    }

    [Fact]
    public async Task N5_3_PlanByWeekNorms_FactByClipsInWeeks_NormChangedMidPeriod()
    {
        var run = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync("Тело"), Monday, "Бег", true));
        run = await SetTimesAsync(run, 3);
        await DoneSessionAsync(run, Monday.AddDays(1)); // 29 сен — неделя 28 сен входит в октябрь
        var next = (await GetWeekAsync(Oct5)).Goals.Single(g => g.SeriesId == run.SeriesId);
        next = await SetTimesAsync(next, 2); // с этой недели — 2 раза; неделя 28 сен — по своей норме 3
        await DoneSessionAsync(next, Oct5.AddDays(1));

        var report = await ReportAsync(Oct1, Oct11);

        Assert.Equal(2, report.WeeksCount);
        var row = report.Series.Single(s => s.Title == "Бег");
        Assert.Equal((5, 2, 40, true), (row.Plan, row.Fact, row.Percent, row.IsHabit));
        Assert.Contains("Тело", row.Roles);
    }

    [Fact]
    public async Task N5_3_ClipsOfDeletedGoal_CountInFact()
    {
        var run = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync("Тело"), Monday, "Бег", true));
        await DoneSessionAsync(run, Monday.AddDays(1));
        await GetWeekAsync(Oct5); // цель следующей недели серии появилась
        (await Client.DeleteAsync($"/api/goals/{run.Id}")).EnsureSuccessStatusCode(); // «только на этой неделе» — занятие осталось скрепкой (C2)

        var row = (await ReportAsync(Oct1, Oct11)).Series.Single(s => s.Title == "Бег");

        Assert.Equal((1, 1), (row.Plan, row.Fact)); // нормы нет у недели без цели, скрепка — есть
    }

    [Fact]
    public async Task N5_4_OneOffGoals_OneLine_OnlyWeeksOfPeriod()
    {
        var work = await RoleIdAsync("Работа");
        var report1 = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(work, Oct5, "Отчёт", false));
        await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(work, Oct5, "Звонок", false));
        await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(work, Oct5.AddDays(7), "Позже", false));
        await PostAsync<GoalResultDto>($"/api/goals/{report1.Id}/toggle", new ToggleGoalRequest(Oct5));

        var report = await ReportAsync(Oct1, Oct11);

        Assert.Equal((2, 1), (report.OneOff.Goals, report.OneOff.Done));
        Assert.DoesNotContain(report.Series, s => s.Title is "Отчёт" or "Звонок");
    }

    [Fact]
    public async Task N5_5_Done_ByDayOfCompletion_GroupedByCategory()
    {
        var books = await CreateCategoryAsync("Книги", "📚");
        var dune = await CreateTaskAsync("Дюна", new SomedayTargetDto(books.Id, 0));
        await SetTaskDoneAsync(dune.Id, true);
        var today = DateOnly.FromDateTime(DateTime.Now);

        var withToday = await ReportAsync(today.AddDays(-1), today);
        var before = await ReportAsync(today.AddDays(-30), today.AddDays(-1));

        var group = withToday.Done.Single();
        Assert.Equal(("Книги", "📚", "Дюна", today), (group.Name, group.Emoji, group.Tasks.Single().Title, group.Tasks.Single().Date));
        Assert.Empty(before.Done);
    }

    [Fact]
    public async Task N5_6_Csv_BomSemicolonCrlf_Quoting()
    {
        var ideas = await CreateCategoryAsync("Идеи; разное");
        var thought = await CreateTaskAsync("Купить \"Дюну\"", new SomedayTargetDto(ideas.Id, 0));
        await SetTaskDoneAsync(thought.Id, true);
        var today = DateOnly.FromDateTime(DateTime.Now);

        var response = await Client.GetAsync($"/api/report.csv?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Раздел;Категория;Дело;Дата отметки;Шагов\r\n", text);
        Assert.Contains($"Выполненное;\"Идеи; разное\";\"Купить \"\"Дюну\"\"\";{today:dd.MM.yyyy};\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        Assert.Contains("CoveyPlanner", response.Content.Headers.ContentDisposition!.ToString());
    }

    [Fact]
    public async Task N5_Empty_Period_StillHasHeaders()
    {
        var future = new DateOnly(2030, 1, 1);

        var report = await ReportAsync(future, future.AddDays(30));
        var csv = await Client.GetStringAsync($"/api/report.csv?from={future:yyyy-MM-dd}&to={future.AddDays(30):yyyy-MM-dd}");

        Assert.Empty(report.Series);
        Assert.Empty(report.Done);
        Assert.Contains("Привычки и постоянные цели", csv);
        Assert.Contains("Раздел;Категория;Дело;Дата отметки;Шагов", csv);
    }
}
