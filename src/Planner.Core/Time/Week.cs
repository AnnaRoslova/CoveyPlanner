namespace Planner.Core.Time;

/// <summary>Неделя с понедельника по воскресенье.</summary>
public static class Week
{
    public const int DaysInWeek = 7;

    /// <summary>Понедельник недели, в которую входит дата.</summary>
    public static DateOnly StartOf(DateOnly date)
    {
        // DayOfWeek: воскресенье = 0, понедельник = 1 ...
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    public static bool IsWeekStart(DateOnly date) => date.DayOfWeek == DayOfWeek.Monday;

    /// <summary>Семь дней недели, начиная с понедельника.</summary>
    public static IEnumerable<DateOnly> Days(DateOnly weekStart) =>
        Enumerable.Range(0, DaysInWeek).Select(weekStart.AddDays);
}
