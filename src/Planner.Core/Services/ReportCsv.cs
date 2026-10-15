using System.Globalization;
using System.Text;

namespace Planner.Core.Services;

/// <summary>
/// Отчёт в CSV для Excel (docs/specs/next-batch.md, N5.6): разделитель «;», строки через CRLF, даты ДД.ММ.ГГГГ, числа без разрядов.
/// Два блока с заголовками — «Привычки и постоянные цели» и «Выполненное». BOM добавляет тот, кто отдаёт файл.
/// </summary>
public static class ReportCsv
{
    public const string NoCategoryName = "без категории";

    public static string Build(Report report)
    {
        var sb = new StringBuilder();
        Line(sb, "Отчёт CoveyPlanner", $"{Date(report.From)} — {Date(report.To)}");
        Line(sb);

        Line(sb, "Привычки и постоянные цели");
        Line(sb, "Раздел", "Название", "Роль/категория", "План", "Факт", "%", "Дата");
        foreach (var row in report.Series)
        {
            Line(sb, row.Series.IsHabit ? "Привычка" : "Постоянная цель", row.Series.Title,
                string.Join(", ", row.Roles.Select(r => r.Name)), Number(row.Plan), Number(row.Fact), Number(row.Percent), "");
        }

        Line(sb, "Разовые цели", $"камней: {Number(report.OneOff.Goals)}", "", "", Number(report.OneOff.Done), "", "");
        Line(sb);

        Line(sb, "Выполненное");
        Line(sb, "Раздел", "Категория", "Дело", "Дата отметки", "Шагов");
        foreach (var group in report.Done)
        {
            var category = group.Category?.Name ?? NoCategoryName;
            foreach (var entry in group.Entries)
            {
                Line(sb, "Выполненное", category, entry.Task.Title, Date(entry.Date),
                    entry.Project is { StepsDone: > 0 } p ? Number(p.StepsDone) : "");
            }
        }

        return sb.ToString();
    }

    /// <summary>Имя файла: CoveyPlanner-отчёт-2026-10-01—2026-10-31.csv.</summary>
    public static string FileName(DateOnly from, DateOnly to) =>
        $"CoveyPlanner-отчёт-{from:yyyy-MM-dd}—{to:yyyy-MM-dd}.csv";

    /// <summary>Значение по правилам CSV: «;», кавычка и перевод строки — в кавычках, кавычка удваивается.</summary>
    public static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static void Line(StringBuilder sb, params string[] values) =>
        sb.Append(string.Join(';', values.Select(Escape))).Append("\r\n");

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
