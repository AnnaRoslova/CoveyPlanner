using System.Globalization;

namespace Planner.Client.Services;

/// <summary>
/// Числа для inline-стилей. Всегда с точкой: в браузере с русской локалью
/// обычный ToString() дал бы «12,5px», и CSS бы это проигнорировал.
/// </summary>
public static class Css
{
    public static string Px(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    public static string Percent(double value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "%";

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
