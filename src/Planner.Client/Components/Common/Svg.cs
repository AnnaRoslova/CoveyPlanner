using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;

namespace Planner.Client.Components.Common;

/// <summary>
/// Подписи для своих SVG-рисунков (схема «Как это устроено», дашборд). Тег подписи SVG в Razor служебный,
/// поэтому подпись выводится строкой разметки (текст экранируется). Атрибута области у неё нет — стили через ::deep.
/// </summary>
public static class Svg
{
    public static MarkupString Text(double x, double y, string css, string text, string anchor = "middle") =>
        new($"<text x=\"{Num(x)}\" y=\"{Num(y)}\" text-anchor=\"{anchor}\" class=\"{css}\">{WebUtility.HtmlEncode(text)}</text>");

    /// <summary>Число для атрибута SVG — всегда с точкой.</summary>
    public static string Num(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
