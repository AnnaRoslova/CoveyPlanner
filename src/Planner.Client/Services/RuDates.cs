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

    private static readonly string[] MonthsShort =
        ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    public static string ShortDay(DateOnly date) => ShortDays[(int)date.DayOfWeek];

    /// <summary>«28 сентября».</summary>
    public static string DayMonth(DateOnly date) => $"{date.Day} {MonthsGenitive[date.Month - 1]}";

    /// <summary>«28 сен – 4 окт 2026».</summary>
    public static string WeekRange(DateOnly weekStart)
    {
        var end = weekStart.AddDays(6);
        return weekStart.Month == end.Month
            ? $"{weekStart.Day}–{end.Day} {MonthsShort[end.Month - 1]} {end.Year}"
            : $"{weekStart.Day} {MonthsShort[weekStart.Month - 1]} – {end.Day} {MonthsShort[end.Month - 1]} {end.Year}";
    }

    public static string Time(TimeOnly time) => time.ToString("HH:mm");

    /// <summary>Понедельник недели, в которую входит дата.</summary>
    public static DateOnly WeekStartOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
