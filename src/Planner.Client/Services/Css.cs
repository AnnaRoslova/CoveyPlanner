using System.Globalization;

namespace Planner.Client.Services;

/// <summary>Значения для разметки (data-атрибутов), не зависящие от языка браузера.</summary>
public static class Css
{
    /// <summary>Дата «2026-09-30» — ключ дня для перетаскивания (DragDropService разбирает её так же).</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Доля 0…1 как CSS-проценты «40%» (с точкой, а не запятой).</summary>
    public static string Percent(double fraction) =>
        (Math.Clamp(fraction, 0, 1) * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
