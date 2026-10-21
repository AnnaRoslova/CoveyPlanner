namespace Planner.Client.Services;

/// <summary>
/// Возраст мысли (docs/specs/batch-ux.md, BS): от последнего изменения (UpdatedAt) — «8 мес», «1 г 3 мес»;
/// подсказка у всех мыслей — «лежит 2 мес · с 14 авг». Старая — срок прошёл от max(UpdatedAt, KeptAt) (BS5). Чистые функции.
/// </summary>
public static class ThoughtAge
{
    /// <summary>Полных месяцев между днями (14 авг → 13 окт — 1 месяц).</summary>
    public static int Months(DateOnly from, DateOnly to) =>
        Math.Max(0, (to.Year - from.Year) * 12 + to.Month - from.Month - (to.Day < from.Day ? 1 : 0));

    /// <summary>«8 мес», «1 г», «1 г 3 мес»; меньше месяца — «N дн».</summary>
    public static string Label(DateOnly from, DateOnly to)
    {
        var months = Months(from, to);
        if (months == 0)
        {
            return $"{Math.Max(0, to.DayNumber - from.DayNumber)} дн";
        }

        return months < 12 ? $"{months} мес" : months % 12 == 0 ? $"{months / 12} г" : $"{months / 12} г {months % 12} мес";
    }

    /// <summary>Подсказка строки ленты: «лежит 2 мес · с 14 авг» (другой год — «с 14 авг 2025»); сегодня — «записано сегодня».</summary>
    public static string Tooltip(DateOnly from, DateOnly to)
    {
        if (from >= to)
        {
            return "записано сегодня";
        }

        var since = from.Year == to.Year ? RuDates.DayMonthShort(from) : $"{RuDates.DayMonthShort(from)} {from.Year}";
        return $"лежит {Label(from, to)} · с {since}";
    }

    /// <summary>Мысль старая: срок <paramref name="months"/> прошёл от последнего изменения или «Оставить».</summary>
    public static bool IsStale(DateTime updatedAtUtc, DateTime? keptAtUtc, int months, DateTime nowUtc)
    {
        var fresh = keptAtUtc is { } kept && kept > updatedAtUtc ? kept : updatedAtUtc;
        return fresh.AddMonths(months) <= nowUtc;
    }

    /// <summary>День по часам пользователя из времени сервера (UTC).</summary>
    public static DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime());
}
