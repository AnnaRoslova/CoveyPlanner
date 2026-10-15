using System.Text;
using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Отчёт за период (N5): JSON для экрана и CSV для Excel. Только чтение.</summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
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
                r.WeekStreak, r.Paused))
            .ToList(),
        new ReportOneOffDto(report.OneOff.Goals, report.OneOff.Done),
        report.Done.Select(g => new ReportDoneGroupDto(
                g.Category?.Id, g.Category?.Name ?? ReportCsv.NoCategoryName, g.Category?.Emoji,
                g.Entries.Select(e => new ReportDoneTaskDto(e.Task.Id, e.Task.Title, e.Date, e.Project?.StepsDone ?? 0)).ToList()))
            .ToList());
}
