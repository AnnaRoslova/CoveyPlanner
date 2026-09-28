using Planner.Core.Errors;

namespace Planner.Core.Entities;

/// <summary>Палитра цветов ролей: спокойные тона, хорошо читаются как фон карточки.</summary>
public static class RolePalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#6b8fb5", // голубой
        "#5f9e8f", // бирюзовый
        "#7fa66a", // зелёный
        "#c2a54f", // горчичный
        "#d08a4e", // оранжевый
        "#c46c6c", // красный
        "#b56f9b", // розовый
        "#8a76b8", // фиолетовый
        "#8c7a6b", // коричневый
        "#7d8791", // серый
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
