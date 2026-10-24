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

    public const string NoRoleName = "без роли";

    /// <summary>Вид пропуска словами — как в интерфейсе.</summary>
    public static string KindName(Enums.SkipKind kind) => kind == Enums.SkipKind.NotNeeded ? "не нужно" : "не сделано";

    public static string Build(Report report)
    {
        var sb = new StringBuilder();
        Line(sb, "Отчёт CoveyPlanner", $"{Date(report.From)} — {Date(report.To)}");
        Line(sb);

        Line(sb, "Привычки и постоянные цели");
        Line(sb, "Раздел", "Название", "Роль/категория", "План", "Факт", "%", "Не сделано", "Не нужно");
        foreach (var row in report.Series)
        {
            Line(sb, row.Series.IsHabit ? "Привычка" : "Постоянная цель", row.Series.Title,
                string.Join(", ", row.Roles.Select(r => r.Name)), Number(row.Plan), Number(row.Fact), Number(row.Percent),
                Number(row.Missed), Number(row.NotNeeded));
        }

        Line(sb, "Разовые цели", $"камней: {Number(report.OneOff.Goals)}", "", "", Number(report.OneOff.Done), "",
            Number(report.OneOff.Missed), Number(report.OneOff.NotNeeded));
        Line(sb);

        // Пропуски (docs/specs/skips.md, O3): по ролям и список причин
        Line(sb, "Роли");
        Line(sb, "Раздел", "Роль", "Сделано", "Не сделано", "Не нужно");
        foreach (var row in report.Roles ?? [])
        {
            Line(sb, "Роль", row.Role?.Name ?? NoRoleName, Number(row.Done), Number(row.Missed), Number(row.NotNeeded));
        }

        Line(sb);
        Line(sb, "Пропуски");
        Line(sb, "Раздел", "Дата", "Дело", "Вид", "Причина", "Роли", "Цель");
        foreach (var skip in report.Skips ?? [])
        {
            Line(sb, "Пропуск", Date(skip.Date), skip.Title, KindName(skip.Kind), skip.Reason ?? "", string.Join(", ", skip.Roles),
                skip.Goal is null ? "" : skip.Goal.Length == 0 ? "цель удалена" : skip.Goal);
        }

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
