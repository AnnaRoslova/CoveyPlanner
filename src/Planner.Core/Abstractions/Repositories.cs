using Planner.Core.Entities;

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

    Task<List<WeeklyGoal>> GetForRoleAsync(int roleId, DateOnly weekStart, CancellationToken ct = default);

    /// <summary>Цели роли за все недели (для удаления роли).</summary>
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

    void Add(GoalSeries series);
}

/// <summary>Сохранение всех изменений одной транзакцией.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
