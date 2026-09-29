namespace Planner.Client.Services;

/// <summary>Часть текста: совпала с поиском или нет.</summary>
public readonly record struct TextSegment(string Text, bool IsMatch);

/// <summary>Поиск по ленте: без учёта регистра, «е» и «ё» считаются одной буквой.</summary>
public static class TextSearch
{
    public static bool Contains(string text, string query) =>
        query.Length == 0 || Normalize(text).Contains(Normalize(query), StringComparison.Ordinal);

    /// <summary>Разбить текст на куски для подсветки всех совпадений.</summary>
    public static IEnumerable<TextSegment> Split(string text, string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            yield return new TextSegment(text, false);
            yield break;
        }

        var haystack = Normalize(text);
        var needle = Normalize(query);
        var position = 0;
        while (position < text.Length)
        {
            var found = haystack.IndexOf(needle, position, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            if (found > position)
            {
                yield return new TextSegment(text[position..found], false);
            }

            yield return new TextSegment(text.Substring(found, needle.Length), true);
            position = found + needle.Length;
        }

        if (position < text.Length)
        {
            yield return new TextSegment(text[position..], false);
        }
    }

    // Длина строки не меняется — индексы совпадений годятся для исходного текста
    private static string Normalize(string value) => value.ToLowerInvariant().Replace('ё', 'е');
}
