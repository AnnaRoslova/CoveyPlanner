using Planner.Core.Sorting;

namespace Planner.Core.Entities;

/// <summary>Категория ленты «Когда-нибудь» (один уровень, без подкатегорий).</summary>
public class Category : ISortable
{
    /// <summary>С запасом: составные эмодзи (семья, флаги) занимают до ~11 UTF-16 символов.</summary>
    public const int EmojiMaxLength = 16;

    private Category()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>Необязательная иконка-эмодзи.</summary>
    public string? Emoji { get; private set; }

    public int SortOrder { get; set; }

    /// <summary>Раскрыта ли категория в аккордеоне.</summary>
    public bool IsExpanded { get; set; }

    public static Category Create(string name, string? emoji = null)
    {
        var category = new Category { IsExpanded = true };
        category.Rename(name);
        category.SetEmoji(emoji);
        return category;
    }

    public void Rename(string name) =>
        Name = TextRules.Required(name, TextRules.NameMaxLength, "Название категории");

    /// <summary>Пустая строка или null — без иконки.</summary>
    public void SetEmoji(string? emoji) =>
        Emoji = TextRules.Optional(emoji, EmojiMaxLength, "Иконка категории");
}
