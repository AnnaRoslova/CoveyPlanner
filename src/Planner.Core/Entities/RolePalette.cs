using Planner.Core.Errors;

namespace Planner.Core.Entities;

/// <summary>
/// Палитра цветов ролей: 18 прохладных пастельных оттенков одинаковой светлоты
/// (docs/role-palette.html). Порядок — как в образце: в выборе цвета это сетка 6×3.
/// </summary>
public static class RolePalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#9fd3bd", // мята
        "#6fbfae", // бирюза
        "#8ac2c4", // морская волна
        "#b9d3b0", // шалфей
        "#cfe3a8", // фисташка
        "#f3e3a6", // сливочный лимон
        "#a9cde8", // пудрово-голубой
        "#8fb4e3", // васильковый
        "#b7c4f0", // барвинок
        "#c9b8ec", // лаванда
        "#dcb8e6", // сирень
        "#e8c1dc", // орхидея
        "#f9d0e0", // зефир
        "#f2b3c6", // роза
        "#f6c6b8", // персик
        "#94b4b5", // серая бирюза
        "#b8c2cc", // серо-голубой
        "#c9c1d6", // серая сирень
    ];

    public static string Validate(string color)
    {
        var normalized = color.Trim().ToLowerInvariant();
        return Colors.Contains(normalized)
            ? normalized
            : throw new DomainException($"Цвет {color} не из палитры.");
    }

    /// <summary>Цвет для новой роли: по кругу палитры.</summary>
    public static string ForIndex(int index) => Colors[index % Colors.Count];
}
