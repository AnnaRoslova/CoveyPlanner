namespace Planner.Client.Services;

/// <summary>
/// Русские названия дней и месяцев. Без CultureInfo("ru-RU"), чтобы не зависеть
/// от того, какие данные ICU браузер загрузит для WebAssembly.
/// </summary>
public static class RuDates
{
    private static readonly string[] ShortDays = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];

    private static readonly string[] MonthsGenitive =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    public static string ShortDay(DateOnly date) => ShortDays[(int)date.DayOfWeek];

    /// <summary>«28 сентября».</summary>
    public static string DayMonth(DateOnly date) => $"{date.Day} {MonthsGenitive[date.Month - 1]}";

    /// <summary>«28 сентября — 4 октября», «5 — 11 октября»; год — только если неделя не в текущем году.</summary>
    public static string WeekRange(DateOnly weekStart)
    {
        var end = weekStart.AddDays(6);
        var range = weekStart.Month == end.Month
            ? $"{weekStart.Day} — {DayMonth(end)}"
            : $"{DayMonth(weekStart)} — {DayMonth(end)}";
        return end.Year == Today.Year ? range : $"{range} {end.Year}";
    }

    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static string Time(TimeOnly time) => time.ToString("HH:mm");

    /// <summary>«15 мин», «1 ч», «1,5 ч», «1 ч 20 мин».</summary>
    public static string Duration(int minutes) => (minutes / 60, minutes % 60) switch
    {
        (0, var m) => $"{m} мин",
        (var h, 0) => $"{h} ч",
        (var h, 30) => $"{h},5 ч",
        var (h, m) => $"{h} ч {m} мин",
    };

    /// <summary>Понедельник недели, в которую входит дата.</summary>
    public static DateOnly WeekStartOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
