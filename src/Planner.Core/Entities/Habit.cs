using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Entities;

/// <summary>
/// Копилка привычки (идея из «Атомных привычек»: банка со скрепками). Скрепка — выполненное дело, привязанное к копилке
/// (через цель или серию — занятия наследуют копилку — или напрямую). Число скрепок не хранится, а считается:
/// <see cref="StartCount"/> + выполненные дела. Полная банка (<see cref="JarSize"/> скрепок) уезжает на полку.
/// Серии недель и месяцев считает <see cref="Habits.HabitStatsCalculator"/>.
/// Вид <see cref="HabitKind.Collection"/> — коллекция (книги, проекты): без нормы и серий, с полкой записей и целью на год
/// (<see cref="Habits.CollectionStatsCalculator"/>); к ней можно привязать категории ленты (<see cref="Categories"/>).
/// Вид можно сменить — скрепки те же, просто по-другому показываются.
/// </summary>
public class Habit : ISortable
{
    public const int MaxYearGoal = 10_000;

    private readonly List<HabitCategory> _categories = [];

    public const int DefaultJarSize = 30;
    public const int MinWeeklyNorm = 1;
    public const int MaxWeeklyNorm = 50;
    public const int MinJarSize = 1;
    public const int MaxJarSize = 1000;
    public const int MaxStartCount = 1_000_000;

    private Habit()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>Необязательная иконка-эмодзи («💪»).</summary>
    public string? Emoji { get; private set; }

    /// <summary>Цвет из палитры ролей.</summary>
    public string Color { get; private set; } = RolePalette.Colors[0];

    /// <summary>Сколько раз в неделю — норма: неделя засчитана, если скрепок не меньше.</summary>
    public int WeeklyNorm { get; private set; } = MinWeeklyNorm;

    /// <summary>Сколько скрепок вмещает банка.</summary>
    public int JarSize { get; private set; } = DefaultJarSize;

    /// <summary>Стартовое число: скрепки, накопленные до копилки (их дат нет — в серии не входят).</summary>
    public int StartCount { get; private set; }

    /// <summary>
    /// Единица количества («мин», «км») — на будущее, когда скрепка будет весить больше единицы
    /// (см. <see cref="TaskItem.Quantity"/>). Пока не используется: null — скрепки поштучно.
    /// </summary>
    public string? Unit { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public int SortOrder { get; set; }

    /// <summary>В архиве: не видна среди копилок и в выборе «В копилку», но счёт сохраняется.</summary>
    public bool IsArchived { get; private set; }

    public static Habit Create(
        string name, string? emoji, string color, int weeklyNorm, int jarSize, int startCount, DateTime now)
    {
        var habit = new Habit { CreatedAt = now };
        habit.Update(name, emoji, color, weeklyNorm, jarSize, startCount);
        return habit;
    }

    public void Update(string name, string? emoji, string color, int weeklyNorm, int jarSize, int startCount)
    {
        Name = TextRules.Required(name, TextRules.NameMaxLength, "Название копилки");
        Emoji = TextRules.Optional(emoji, Category.EmojiMaxLength, "Иконка копилки");
        Color = RolePalette.Validate(color);
        WeeklyNorm = weeklyNorm is >= MinWeeklyNorm and <= MaxWeeklyNorm
            ? weeklyNorm
            : throw new DomainException($"Норма — от {MinWeeklyNorm} до {MaxWeeklyNorm} раз в неделю.");
        JarSize = jarSize is >= MinJarSize and <= MaxJarSize
            ? jarSize
            : throw new DomainException($"В банке — от {MinJarSize} до {MaxJarSize} скрепок.");
        StartCount = startCount is >= 0 and <= MaxStartCount
            ? startCount
            : throw new DomainException($"Стартовое число — от 0 до {MaxStartCount}.");
    }

    public void SetArchived(bool archived) => IsArchived = archived;

    /// <summary>Привычка или коллекция.</summary>
    public HabitKind Kind { get; private set; } = HabitKind.Habit;

    public bool IsCollection => Kind == HabitKind.Collection;

    /// <summary>Цель коллекции на год («12 в 2026»); null — без цели.</summary>
    public int? YearGoal { get; private set; }

    /// <summary>
    /// Категории ленты коллекции (таблица HabitCategories): выполненное дело, происходящее из них, само попадает в коллекцию.
    /// Действуют, только пока копилка — коллекция (сменили вид на привычку — привязка хранится, но не работает).
    /// </summary>
    public IReadOnlyCollection<HabitCategory> Categories => _categories;

    public IReadOnlyList<int> CategoryIds => _categories.Select(c => c.CategoryId).Order().ToList();

    public void SetKind(HabitKind kind) =>
        Kind = Enum.IsDefined(kind) ? kind : throw new DomainException("Неизвестный вид копилки.");

    public void SetYearGoal(int? goal) => YearGoal = goal switch
    {
        null => null,
        >= 1 and <= MaxYearGoal => goal,
        _ => throw new DomainException($"Цель на год — от 1 до {MaxYearGoal}."),
    };

    /// <summary>Задать категории (повторы не считаются); уже стоящие остаются теми же записями.</summary>
    public void SetCategories(IEnumerable<int> categoryIds)
    {
        var wanted = categoryIds.ToHashSet();
        _categories.RemoveAll(c => !wanted.Contains(c.CategoryId));
        _categories.AddRange(wanted.Where(id => _categories.All(c => c.CategoryId != id)).Select(id => new HabitCategory(id)));
    }

    /// <summary>Отдать категорию другой коллекции: у категории — не больше одной копилки.</summary>
    public void RemoveCategory(int categoryId) => _categories.RemoveAll(c => c.CategoryId == categoryId);
}
