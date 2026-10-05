namespace Planner.Core.Habits;

/// <summary>Запись коллекции до подсчёта: дело и его шаги.</summary>
/// <param name="FirstStepOn">День первого отмеченного шага; null — шагов не отмечали.</param>
public sealed record CollectionItem(int TaskId, string Title, DateOnly CompletedOn, int StepCount = 0, DateOnly? FirstStepOn = null);

/// <summary>Запись на полке: «название · дата»; с шагами — «14 шагов · 5 недель».</summary>
/// <param name="SpanDays">Сколько дней от первого отмеченного шага до завершения, включительно (тот же день — 1); null — шагов не отмечали.</param>
public sealed record CollectionEntry(int TaskId, string Title, DateOnly CompletedOn, int StepCount, int? SpanDays);

public sealed record CollectionYear(int Year, int Count);

/// <summary>Коллекция: всего за всё время (со стартовым числом), в этом году и цель года, разбивка по годам, записи — свежие сверху.</summary>
public sealed record CollectionStats(
    int Total, int ThisYear, int? YearGoal, IReadOnlyList<CollectionYear> Years, IReadOnlyList<CollectionEntry> Entries);

/// <summary>
/// Коллекция (книги, проекты, поездки): без нормы, серий и каникул. Скрепка — выполненное дело; счёт за всё время
/// не обнуляется (стартовое число — только в общем счёте, у него нет года), плюс разбивка по годам завершения.
/// </summary>
public static class CollectionStatsCalculator
{
    public static CollectionStats Calculate(IReadOnlyCollection<CollectionItem> items, int startCount, int? yearGoal, DateOnly today)
    {
        var entries = items
            .OrderByDescending(i => i.CompletedOn).ThenByDescending(i => i.TaskId)
            .Select(i => new CollectionEntry(i.TaskId, i.Title, i.CompletedOn, i.StepCount, SpanDays(i)))
            .ToList();
        var years = items.GroupBy(i => i.CompletedOn.Year).OrderBy(g => g.Key).Select(g => new CollectionYear(g.Key, g.Count())).ToList();

        return new CollectionStats(
            startCount + items.Count,
            items.Count(i => i.CompletedOn.Year == today.Year),
            yearGoal,
            years,
            entries);
    }

    /// <summary>От первого отмеченного шага до завершения дела, включительно; шаг «позже» завершения — всё равно 1 день.</summary>
    private static int? SpanDays(CollectionItem item) =>
        item.FirstStepOn is { } first ? Math.Max(1, item.CompletedOn.DayNumber - first.DayNumber + 1) : null;
}
