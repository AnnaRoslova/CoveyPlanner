using System.Text.RegularExpressions;

namespace Planner.Client.Services;

/// <summary>Понятая дата: день или месяц целиком (тогда <see cref="Date"/> — его первое число).</summary>
public readonly record struct DateQueryResult(DateOnly Date, bool IsMonth);

/// <summary>
/// Дата, набранная словами или цифрами (docs/specs/batch-3.md, J4): «12.03», «12.03.2027», «12.03.27», «2027-03-12»,
/// «12 марта», «12 мар 2027», «март 2027», «март». Без года — текущий год. Непонятное — null, без ошибок.
/// </summary>
public static partial class DateQuery
{
    private static readonly string[] Nominative =
        ["январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"];

    private static readonly string[] Genitive =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    public static DateQueryResult? Parse(string? text, DateOnly today)
    {
        var query = (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        if (query.Length == 0)
        {
            return null;
        }

        if (Digits().Match(query) is { Success: true } digits)
        {
            return Day(Year(digits.Groups["y"].Value, today), Number(digits.Groups["m"].Value), Number(digits.Groups["d"].Value));
        }

        if (Iso().Match(query) is { Success: true } iso)
        {
            return Day(Number(iso.Groups["y"].Value), Number(iso.Groups["m"].Value), Number(iso.Groups["d"].Value));
        }

        if (Words().Match(query) is { Success: true } words && MonthOf(words.Groups["month"].Value) is { } month)
        {
            var year = Year(words.Groups["y"].Value, today);
            return words.Groups["d"].Success
                ? Day(year, month, Number(words.Groups["d"].Value))
                : year is { } y ? new DateQueryResult(new DateOnly(y, month, 1), IsMonth: true) : null;
        }

        return null;
    }

    /// <summary>Номер месяца по слову: полное название в любом падеже или начало от трёх букв («окт», «сент.»).</summary>
    private static int? MonthOf(string word)
    {
        word = word.TrimEnd('.');
        if (word.Length < 3)
        {
            return null;
        }

        for (var i = 0; i < Nominative.Length; i++)
        {
            if (Nominative[i].StartsWith(word, StringComparison.Ordinal) || Genitive[i].StartsWith(word, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return null;
    }

    /// <summary>Год: пусто — текущий, две цифры — 20xx; вне 1900–2100 — непонятно.</summary>
    private static int? Year(string text, DateOnly today)
    {
        if (text.Length == 0)
        {
            return today.Year;
        }

        var year = Number(text);
        if (text.Length == 2)
        {
            year += 2000;
        }

        return year is >= 1900 and <= 2100 ? year : null;
    }

    private static DateQueryResult? Day(int? year, int month, int day) =>
        year is { } y && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(y, month)
            ? new DateQueryResult(new DateOnly(y, month, day), IsMonth: false)
            : null;

    private static int Number(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>«12.03», «12/03/2027», «1.3.27».</summary>
    [GeneratedRegex(@"^(?<d>\d{1,2})[./](?<m>\d{1,2})(?:[./](?<y>\d{4}|\d{2}))?\.?$")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})$")]
    private static partial Regex Iso();

    /// <summary>«12 марта», «12 мар. 2027 г.», «март 2027», «март».</summary>
    [GeneratedRegex(@"^(?:(?<d>\d{1,2})\s*)?(?<month>\p{L}+\.?)(?:\s*(?<y>\d{4}|\d{2}))?(?:\s*г\.?)?$")]
    private static partial Regex Words();
}
