using Planner.Core.Errors;

namespace Planner.Core.Entities;

/// <summary>Проверки текстовых полей сущностей.</summary>
public static class TextRules
{
    public const int TitleMaxLength = 500;
    public const int NotesMaxLength = 10_000;
    public const int NameMaxLength = 100;
    public const int MissionMaxLength = 5000;
    public const int VisionMaxLength = 200;

    /// <summary>Обрезает пробелы; пустая строка или превышение длины — ошибка.</summary>
    public static string Required(string? value, int maxLength, string fieldName)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new DomainException($"{fieldName}: значение не может быть пустым.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{fieldName}: не длиннее {maxLength} символов.");
        }

        return trimmed;
    }

    /// <summary>Пустое значение превращается в null.</summary>
    public static string? Optional(string? value, int maxLength, string fieldName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new DomainException($"{fieldName}: не длиннее {maxLength} символов.");
    }

    public const int MaxDurationMinutes = 24 * 60;

    /// <summary>Длительность в минутах: null — не указана, иначе от 1 минуты до суток.</summary>
    public static int? Duration(int? minutes) => minutes switch
    {
        null => null,
        >= 1 and <= MaxDurationMinutes => minutes,
        _ => throw new DomainException($"Длительность — от 1 до {MaxDurationMinutes} минут."),
    };
}
