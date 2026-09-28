using Planner.Core.Errors;

namespace Planner.Core.Entities;

/// <summary>Палитра цветов ролей «Лотос» (docs/design-mockup.html): розовые и бирюзовые пастели.</summary>
public static class RolePalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#f29ea6", // коралловый розовый
        "#f4c3cf", // румянец
        "#6fbfae", // бирюзово-зелёный
        "#94b4b5", // серая бирюза
        "#a8dcd0", // мята
        "#f7d3db", // бледно-розовый
        "#8ac2c4", // морская волна
        "#e9b0b8", // пыльная роза
        "#f5b7a5", // персиковый
        "#b9d3b0", // шалфей
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
