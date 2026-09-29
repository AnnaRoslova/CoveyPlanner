using Planner.Core.Errors;

namespace Planner.Core.Entities;

/// <summary>
/// Палитра цветов ролей «Лотос» (docs/design-mockup.html): зелёных и бирюзовых больше,
/// розовые — холодные светлые акценты.
/// </summary>
public static class RolePalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#9fd3bd", // мятно-зелёный
        "#6fbfae", // бирюзово-зелёный
        "#94b4b5", // серая бирюза
        "#a8dcd0", // мята
        "#8ac2c4", // морская волна
        "#7fc3b4", // нефритовый
        "#b9d3b0", // шалфей
        "#efb8cc", // зефирный розовый
        "#f5cddd", // бледный зефир
        "#e8bfd3", // сиреневато-розовый
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
