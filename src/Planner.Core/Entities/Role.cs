using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Entities;

/// <summary>
/// Роль Кови («кто я»): «Я сама», «Семья», «Работа»...
/// Четыре измерения «Затачивания пилы» — тоже роли, но системные (SawDimension != null):
/// их нельзя удалить или переименовать, и цели у них устроены так же.
/// </summary>
public class Role : ISortable
{
    /// <summary>Рекомендуемое число целей на неделю (лимит мягкий — только подсказка).</summary>
    public const int RecommendedGoalsPerWeek = 3;

    private Role()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    public string Color { get; private set; } = RolePalette.Colors[0];

    public int SortOrder { get; set; }

    public SawDimension? SawDimension { get; private set; }

    public bool IsSharpenSaw => SawDimension is not null;

    public static Role Create(string name, string color) => new()
    {
        Name = TextRules.Required(name, TextRules.NameMaxLength, "Название роли"),
        Color = RolePalette.Validate(color),
    };

    public static Role CreateSawDimension(SawDimension dimension, string name, string color) => new()
    {
        Name = name,
        Color = RolePalette.Validate(color),
        SawDimension = dimension,
        SortOrder = (int)dimension - 1,
    };

    public void Rename(string name)
    {
        EnsureEditable();
        Name = TextRules.Required(name, TextRules.NameMaxLength, "Название роли");
    }

    public void SetColor(string color) => Color = RolePalette.Validate(color);

    public void EnsureEditable()
    {
        if (IsSharpenSaw)
        {
            throw new DomainException("Измерения «Затачивания пилы» нельзя переименовать или удалить.");
        }
    }
}
