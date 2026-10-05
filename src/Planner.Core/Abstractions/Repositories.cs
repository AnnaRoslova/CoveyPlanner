using Planner.Core.Entities;
using Planner.Core.Habits;

namespace Planner.Core.Abstractions;

// Хранилище спрятано за интерфейсами: сервисы Core не знают про EF и SQLite.
// Списки возвращаются уже отсортированными по SortOrder, если не сказано иное.

public interface ITaskRepository
{
    Task<TaskItem?> FindAsync(int id, CancellationToken ct = default);

    /// <summary>Все дела ленты (для экрана «Когда-нибудь»).</summary>
    Task<List<TaskItem>> GetSomedayAsync(CancellationToken ct = default);

    /// <summary>Дела ленты в категории; categoryId = null — «неразобранное».</summary>
    Task<List<TaskItem>> GetSomedayInCategoryAsync(int? categoryId, CancellationToken ct = default);

    /// <summary>Дела дня без времени (включая выполненные) — те, у которых ручной порядок.</summary>
    Task<List<TaskItem>> GetUntimedOnDayAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Все дела плана (Planned/Done) с датой в диапазоне включительно.</summary>
    Task<List<TaskItem>> GetPlannedAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Шаги дела по порядку.</summary>
    Task<List<TaskItem>> GetStepsAsync(int parentId, CancellationToken ct = default);

    /// <summary>Корзина, свежие сверху.</summary>
    Task<List<TaskItem>> GetTrashedAsync(CancellationToken ct = default);

    void Add(TaskItem task);

    void Remove(TaskItem task);
}

public interface ICategoryRepository
{
    Task<Category?> FindAsync(int id, CancellationToken ct = default);

    Task<List<Category>> GetAllAsync(CancellationToken ct = default);

    void Add(Category category);

    void Remove(Category category);
}

public interface IRoleRepository
{
    Task<Role?> FindAsync(int id, CancellationToken ct = default);

    /// <summary>Все роли: сначала 4 измерения пилы, затем пользовательские.</summary>
    Task<List<Role>> GetAllAsync(CancellationToken ct = default);

    void Add(Role role);

    void Remove(Role role);
}

/// <summary>Цели подгружаются вместе с занятиями (Sessions).</summary>
public interface IGoalRepository
{
    Task<WeeklyGoal?> FindAsync(int id, CancellationToken ct = default);

    Task<List<WeeklyGoal>> GetForWeekAsync(DateOnly weekStart, CancellationToken ct = default);

    /// <summary>Цели, у которых есть роль <paramref name="roleId"/>, на неделю — по их месту в этой роли.</summary>
    Task<List<WeeklyGoal>> GetForRoleAsync(int roleId, DateOnly weekStart, CancellationToken ct = default);

    /// <summary>Цели с этой ролью за все недели (для удаления роли).</summary>
    Task<List<WeeklyGoal>> GetAllForRoleAsync(int roleId, CancellationToken ct = default);

    /// <summary>Цели серии с недели <paramref name="fromWeek"/> включительно, по неделям.</summary>
    Task<List<WeeklyGoal>> GetInSeriesAsync(int seriesId, DateOnly fromWeek, CancellationToken ct = default);

    /// <summary>Цель серии на неделю (у серии на неделе не больше одной цели).</summary>
    Task<WeeklyGoal?> FindInSeriesAsync(int seriesId, DateOnly weekStart, CancellationToken ct = default);

    void Add(WeeklyGoal goal);

    void Remove(WeeklyGoal goal);
}

/// <summary>Серии целей (постоянные цели «каждую неделю» и связи перенесённых занятий).</summary>
public interface IGoalSeriesRepository
{
    /// <summary>Постоянные серии, у которых могут появиться новые недели.</summary>
    Task<List<GoalSeries>> GetRecurringAsync(CancellationToken ct = default);

    /// <summary>Серии, в шаблоне которых есть роль (для удаления роли).</summary>
    Task<List<GoalSeries>> GetWithRoleAsync(int roleId, CancellationToken ct = default);

    void Add(GoalSeries series);

    void Remove(GoalSeries series);
}

/// <summary>Сохранение всех изменений одной транзакцией.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Копилки привычек.</summary>
public interface IHabitRepository
{
    Task<Habit?> FindAsync(int id, CancellationToken ct = default);

    /// <summary>Все копилки (и в архиве) по порядку.</summary>
    Task<List<Habit>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Возможные скрепки одним запросом: выполненные дела (корзина не считается) с копилкой или из категорий
    /// <paramref name="collectionCategoryIds"/> (привязанных к коллекциям), с числом шагов и первым отмеченным шагом.
    /// </summary>
    Task<List<HabitClipSource>> GetClipsAsync(IReadOnlyCollection<int> collectionCategoryIds, CancellationToken ct = default);

    void Add(Habit habit);

    void Remove(Habit habit);
}

/// <summary>Каникулы копилок.</summary>
public interface IVacationRepository
{
    Task<Vacation?> FindAsync(int id, CancellationToken ct = default);

    /// <summary>Все каникулы, новые (по дате начала) сверху.</summary>
    Task<List<Vacation>> GetAllAsync(CancellationToken ct = default);

    void Add(Vacation vacation);

    void Remove(Vacation vacation);
}
