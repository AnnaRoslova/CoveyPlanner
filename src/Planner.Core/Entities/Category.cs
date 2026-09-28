using Planner.Core.Sorting;

namespace Planner.Core.Entities;

/// <summary>Категория ленты «Когда-нибудь» (один уровень, без подкатегорий).</summary>
public class Category : ISortable
{
    public const string InboxName = "Входящие";

    private Category()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    public int SortOrder { get; set; }

    /// <summary>Раскрыта ли категория в аккордеоне.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>Системная категория «Входящие»: её нельзя удалить, туда попадают дела без категории.</summary>
    public bool IsSystem { get; private set; }

    public static Category Create(string name) => new()
    {
        Name = TextRules.Required(name, TextRules.NameMaxLength, "Название категории"),
        IsExpanded = true,
    };

    public static Category CreateInbox() => new()
    {
        Name = InboxName,
        IsExpanded = true,
        IsSystem = true,
    };

    public void Rename(string name) =>
        Name = TextRules.Required(name, TextRules.NameMaxLength, "Название категории");
}
