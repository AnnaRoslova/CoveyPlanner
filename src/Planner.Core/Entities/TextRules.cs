using Planner.Core.Errors;

namespace Planner.Core.Entities;

/// <summary>Проверки текстовых полей сущностей.</summary>
public static class TextRules
{
    public const int TitleMaxLength = 500;
    public const int NotesMaxLength = 10_000;
    public const int NameMaxLength = 100;

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
}
