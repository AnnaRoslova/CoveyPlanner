using Planner.Core.Entities;
using Planner.Core.Habits;

namespace Planner.Core.Abstractions;

// Хранилище спрятано за интерфейсами: сервисы Core не знают про EF и SQLite.
// Списки возвращаются уже отсортированными по SortOrder, если не сказано иное.

public interface ITaskRepository
{
    Task<TaskItem?> FindAsync(int id, CancellationToken ct = default);

    /// <summary>Дела по списку Id (порядок не задан).</summary>
    Task<List<TaskItem>> FindManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Все дела ленты (для экрана «Когда-нибудь»).</summary>
    Task<List<TaskItem>> GetSomedayAsync(CancellationToken ct = default);

    /// <summary>Открытые проекты в плане (с днём или без, любой недели) — для «🛠 Проекты» (N6.9); проекты ленты приходят с лентой.</summary>
    Task<List<TaskItem>> GetPlannedProjectsAsync(CancellationToken ct = default);

    /// <summary>Дела «на неделе, без дня» (N4) недели <paramref name="weekStart"/> — в плане и выполненные, по порядку.</summary>
    Task<List<TaskItem>> GetWithoutDayOnWeekAsync(DateOnly weekStart, CancellationToken ct = default);

    /// <summary>Дела ленты в категории; categoryId = null — «неразобранное».</summary>
    Task<List<TaskItem>> GetSomedayInCategoryAsync(int? categoryId, CancellationToken ct = default);

    /// <summary>Дела дня без времени (включая выполненные) — те, у которых ручной порядок.</summary>
    Task<List<TaskItem>> GetUntimedOnDayAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Все дела плана (Planned/Done) с датой в диапазоне включительно.</summary>
    Task<List<TaskItem>> GetPlannedAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Шаги дела по порядку.</summary>
    Task<List<TaskItem>> GetStepsAsync(int parentId, CancellationToken ct = default);

    /// <summary>Шаги нескольких дел (для сводок проектов), по родителю и порядку.</summary>
    Task<List<TaskItem>> GetStepsOfAsync(IReadOnlyCollection<int> parentIds, CancellationToken ct = default);

    /// <summary>«Выполненное»: выполненные дела из ленты без родителя, свежие сверху.</summary>
    Task<List<TaskItem>> GetDoneFromSomedayAsync(CancellationToken ct = default);

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
    Task<GoalSeries?> FindAsync(int id, CancellationToken ct = default);

    /// <summary>Постоянные серии (и закончившиеся): у идущих могут появиться новые недели, все они — возможные привычки.</summary>
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

/// <summary>Скрепки и нормы привычек (постоянных серий) — только чтение, одним запросом каждое.</summary>
public interface IHabitRepository
{
    /// <summary>Выполненные занятия целей постоянных серий (корзина и шаги не считаются).</summary>
    Task<List<HabitClip>> GetClipsAsync(CancellationToken ct = default);

    /// <summary>«Раз в неделю» каждой цели постоянных серий — норма её недели.</summary>
    Task<List<HabitWeekNorm>> GetWeekNormsAsync(CancellationToken ct = default);
}

/// <summary>Каникулы привычек.</summary>
public interface IVacationRepository
{
    Task<Vacation?> FindAsync(int id, CancellationToken ct = default);

    /// <summary>Все каникулы, новые (по дате начала) сверху.</summary>
    Task<List<Vacation>> GetAllAsync(CancellationToken ct = default);

    void Add(Vacation vacation);

    void Remove(Vacation vacation);
}
