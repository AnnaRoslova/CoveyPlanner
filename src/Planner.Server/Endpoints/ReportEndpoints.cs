using System.Text;
using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Отчёт за период (N5): JSON для экрана и CSV для Excel. Только чтение.</summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        // Дашборд (batch-ux.md, BD8) — тот же период, одним ответом
        api.MapGet("/dashboard", async (DateOnly from, DateOnly to, DashboardService service, CancellationToken ct) =>
            ToDto(await service.GetAsync(from, to, ct)));

        api.MapGet("/report", async (DateOnly from, DateOnly to, ReportService service, CancellationToken ct) =>
            ToDto(await service.GetAsync(from, to, ct)));

        // UTF-8 с BOM — Excel тогда открывает кириллицу без вопросов
        api.MapGet("/report.csv", async (DateOnly from, DateOnly to, ReportService service, CancellationToken ct) =>
        {
            var report = await service.GetAsync(from, to, ct);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReportCsv.Build(report))).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", ReportCsv.FileName(report.From, report.To));
        });
    }

    private static ReportDto ToDto(Report report) => new(
        report.From,
        report.To,
        report.Weeks.Count,
        report.Series.Select(r => new ReportSeriesDto(
                r.Series.Id, r.Series.Title, r.Roles.Select(x => x.Name).ToList(), r.Series.IsHabit, r.Plan, r.Fact, r.Percent,
                r.WeekStreak, r.Paused, r.Missed, r.NotNeeded))
            .ToList(),
        new ReportOneOffDto(report.OneOff.Goals, report.OneOff.Done, report.OneOff.Missed, report.OneOff.NotNeeded),
        report.Done.Select(g => new ReportDoneGroupDto(
                g.Category?.Id, g.Category?.Name ?? ReportCsv.NoCategoryName, g.Category?.Emoji,
                g.Entries.Select(e => new ReportDoneTaskDto(e.Task.Id, e.Task.Title, e.Date, e.Project?.StepsDone ?? 0)).ToList()))
            .ToList(),
        (report.Roles ?? []).Select(r => new ReportRoleDto(
                r.Role?.Id, r.Role?.Name ?? ReportCsv.NoRoleName, r.Role?.Color ?? NoRoleColor, r.Done, r.Missed, r.NotNeeded))
            .ToList(),
        (report.Skips ?? []).Select(s => new ReportSkipDto(s.Date, s.Title, s.Kind.ToString(), s.Reason, s.Roles, s.Goal)).ToList());

    /// <summary>Цвет «без роли» — нейтральный серый из палитры ролей.</summary>
    private const string NoRoleColor = "#b8c2cc";

    private static DashboardDto ToDto(Dashboard d) => new(
        d.From,
        d.To,
        d.Weeks,
        d.Habits.Select(h => new DashboardHabitDto(
                h.Habit.Series.Id, h.Habit.Series.Title, h.Habit.Roles[0].Color, h.Habit.IsArchived,
                h.Habit.Stats.WeekStreak, h.Habit.Stats.BestWeekStreak, h.Clips))
            .ToList(),
        d.Vacations.Select(v => new DashboardVacationDto(v.From, v.To, v.HabitIds)).ToList(),
        d.Saw.Select(RoleCount).ToList(),
        d.Roles.Select(RoleCount).ToList(),
        d.Quadrants.Select(q => new DashboardQuadrantDto(q.WeekStart, q.Second, q.Marked, q.Unmarked)).ToList(),
        new DashboardStaleDto(d.Stale.Months, d.Stale.Count,
            d.Stale.Oldest.Select(t => new DashboardThoughtDto(t.Id, t.Title, DateTime.SpecifyKind(t.UpdatedAt, DateTimeKind.Utc))).ToList()),
        d.Skips is { } s ? new DashboardSkipsDto(s.Missed, s.NotNeeded, s.TopReasons.Select(x => new DashboardReasonDto(x.Reason, x.Count)).ToList()) : null);

    private static DashboardRoleDto RoleCount(DashboardRoleCount c) =>
        new(c.Role?.Id, c.Role?.Name ?? "без роли", c.Role?.Color ?? NoRoleColor, c.Role?.IsSharpenSaw ?? false, c.Count);
}
