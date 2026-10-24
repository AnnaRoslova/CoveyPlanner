using Microsoft.EntityFrameworkCore;
using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Habits;

namespace Planner.Server.Data.Repositories;

public class EfTaskRepository(PlannerDbContext db) : ITaskRepository
{
    public Task<TaskItem?> FindAsync(int id, CancellationToken ct = default) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<List<TaskItem>> FindManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

    public Task<List<TaskItem>> GetSomedayAsync(CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Someday && t.ParentId == null)
            .OrderBy(t => t.CategoryId).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetPlannedProjectsAsync(CancellationToken ct = default) =>
        db.Tasks.Where(t => t.IsProject && t.State == TaskState.Planned && t.ParentId == null)
            .OrderBy(t => t.PlannedDate).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetWithoutDayOnWeekAsync(DateOnly weekStart, CancellationToken ct = default) =>
        db.Tasks.Where(t => t.PlannedWeek == weekStart && t.PlannedDate == null
                            && (t.State == TaskState.Planned || t.State == TaskState.Done || t.State == TaskState.Skipped))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetSomedayInCategoryAsync(int? categoryId, CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Someday && t.CategoryId == categoryId && t.ParentId == null)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetUntimedOnDayAsync(DateOnly date, CancellationToken ct = default) =>
        db.Tasks.Where(t => (t.State == TaskState.Planned || t.State == TaskState.Done || t.State == TaskState.Skipped)
                            && t.PlannedDate == date && t.StartTime == null)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

    /// <summary>Порядок как на экране: сначала дела со временем (по времени), затем без времени (по SortOrder).</summary>
    public Task<List<TaskItem>> GetPlannedAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        db.Tasks.Where(t => (t.State == TaskState.Planned || t.State == TaskState.Done || t.State == TaskState.Skipped)
                            && t.PlannedDate >= from && t.PlannedDate <= to)
            .OrderBy(t => t.PlannedDate)
            .ThenBy(t => t.StartTime == null).ThenBy(t => t.StartTime)
            .ThenBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<DateOnly>> GetBusyDaysAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        db.Tasks.Where(t => (t.State == TaskState.Planned || t.State == TaskState.Done || t.State == TaskState.Skipped)
                            && t.PlannedDate >= from && t.PlannedDate <= to)
            .Select(t => t.PlannedDate!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

    /// <summary>
    /// Корзина: дела и шаги, удалённые отдельно; шаги, ушедшие в корзину вместе с проектом (тот же TrashedAt), — в его строке.
    /// </summary>
    public Task<List<TaskItem>> GetNotTrashedAsync(CancellationToken ct = default) =>
        db.Tasks.AsNoTracking().Where(t => t.State != TaskState.Trashed).ToListAsync(ct);

    public Task<List<TaskItem>> GetTrashedAsync(CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Trashed
                            && (t.ParentId == null
                                || !db.Tasks.Any(p => p.Id == t.ParentId && p.State == TaskState.Trashed
                                                      && p.TrashedAt == t.TrashedAt)))
            .OrderByDescending(t => t.TrashedAt)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetStepsAsync(int parentId, CancellationToken ct = default) =>
        db.Tasks.Where(t => t.ParentId == parentId).OrderBy(t => t.StepOrder).ThenBy(t => t.Id).ToListAsync(ct);

    public Task<List<TaskItem>> GetStepsOfAsync(IReadOnlyCollection<int> parentIds, CancellationToken ct = default) =>
        db.Tasks.Where(t => t.ParentId != null && parentIds.Contains(t.ParentId.Value))
            .OrderBy(t => t.ParentId).ThenBy(t => t.StepOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetDoneFromSomedayAsync(CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Done && t.IsFromSomeday && t.ParentId == null)
            .OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetCompletedAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        db.Tasks.AsNoTracking()
            .Where(t => t.State == TaskState.Done && t.CompletedAt >= fromUtc && t.CompletedAt < toUtc)
            .ToListAsync(ct);

    public void Add(TaskItem task) => db.Tasks.Add(task);

    public void Remove(TaskItem task) => db.Tasks.Remove(task);
}

public class EfCategoryRepository(PlannerDbContext db) : ICategoryRepository
{
    public Task<Category?> FindAsync(int id, CancellationToken ct = default) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Category>> GetAllAsync(CancellationToken ct = default) =>
        db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);

    public void Add(Category category) => db.Categories.Add(category);

    public void Remove(Category category) => db.Categories.Remove(category);
}

public class EfRoleRepository(PlannerDbContext db) : IRoleRepository
{
    public Task<Role?> FindAsync(int id, CancellationToken ct = default) =>
        db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<Role>> GetAllAsync(CancellationToken ct = default) =>
        db.Roles
            .OrderBy(r => r.SawDimension == null) // false (пила) идёт раньше true
            .ThenBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync(ct);

    public void Add(Role role) => db.Roles.Add(role);

    public void Remove(Role role) => db.Roles.Remove(role);
}

public class EfGoalRepository(PlannerDbContext db) : IGoalRepository
{
    private IQueryable<WeeklyGoal> Goals =>
        db.WeeklyGoals.Include(g => g.Links).ThenInclude(l => l.Task).Include(g => g.Series).Include(g => g.Project);

    public Task<WeeklyGoal?> FindAsync(int id, CancellationToken ct = default) =>
        Goals.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<List<WeeklyGoal>> GetForWeekAsync(DateOnly weekStart, CancellationToken ct = default) =>
        Goals.Where(g => g.WeekStart == weekStart).OrderBy(g => g.Id).ToListAsync(ct);

    public async Task<List<WeeklyGoal>> GetForRoleAsync(int roleId, DateOnly weekStart, CancellationToken ct = default)
    {
        // Место цели — у её роли (владеемая запись), сортируем уже в памяти
        var found = await Goals.Where(g => g.WeekStart == weekStart && g.Roles.Any(r => r.RoleId == roleId)).ToListAsync(ct);
        return found.OrderBy(g => g.RoleLink(roleId)!.SortOrder).ToList();
    }

    public Task<List<WeeklyGoal>> GetAllForRoleAsync(int roleId, CancellationToken ct = default) =>
        Goals.Where(g => g.Roles.Any(r => r.RoleId == roleId)).ToListAsync(ct);

    public Task<List<WeeklyGoal>> GetAllAsync(CancellationToken ct = default) => Goals.ToListAsync(ct);

    public Task<List<WeeklyGoal>> GetInSeriesAsync(int seriesId, DateOnly fromWeek, CancellationToken ct = default) =>
        Goals.Where(g => g.SeriesId == seriesId && g.WeekStart >= fromWeek).OrderBy(g => g.WeekStart).ToListAsync(ct);

    public Task<WeeklyGoal?> FindInSeriesAsync(int seriesId, DateOnly weekStart, CancellationToken ct = default) =>
        Goals.FirstOrDefaultAsync(g => g.SeriesId == seriesId && g.WeekStart == weekStart, ct);

    public void Add(WeeklyGoal goal) => db.WeeklyGoals.Add(goal);

    public void Remove(WeeklyGoal goal) => db.WeeklyGoals.Remove(goal);
}

public class EfGoalSeriesRepository(PlannerDbContext db) : IGoalSeriesRepository
{
    public Task<GoalSeries?> FindAsync(int id, CancellationToken ct = default) =>
        db.GoalSeries.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<GoalSeries>> GetRecurringAsync(CancellationToken ct = default) =>
        db.GoalSeries.Where(s => s.IsRecurring).ToListAsync(ct);

    public Task<List<GoalSeries>> GetWithRoleAsync(int roleId, CancellationToken ct = default) =>
        db.GoalSeries.Where(s => s.Roles.Any(r => r.RoleId == roleId)).ToListAsync(ct);

    public void Add(GoalSeries series) => db.GoalSeries.Add(series);

    public void Remove(GoalSeries series) => db.GoalSeries.Remove(series);
}

public class EfHabitRepository(PlannerDbContext db) : IHabitRepository
{
    /// <summary>Один запрос: выполненные занятия целей постоянных серий (дело — занятие одной цели; шаги-занятия тоже считаются).</summary>
    public async Task<List<HabitClip>> GetClipsAsync(CancellationToken ct = default)
    {
        var sessions = await db.Set<TaskGoal>().AsNoTracking()
            .Where(l => l.Task.State == TaskState.Done
                        && l.Goal.SeriesId != null && l.Goal.Series!.IsRecurring)
            .Select(l => new HabitClip(l.Goal.SeriesId!.Value, l.Task.PlannedDate, l.Goal.WeekStart))
            .ToListAsync(ct);

        // Выполненные занятия удалённых целей серии остаются её скрепками (у них всегда есть день)
        var kept = await db.Tasks.AsNoTracking()
            .Where(t => t.State == TaskState.Done && t.ClipSeriesId != null && t.PlannedDate != null
                        && db.GoalSeries.Any(s => s.Id == t.ClipSeriesId && s.IsRecurring))
            .Select(t => new { SeriesId = t.ClipSeriesId!.Value, Date = t.PlannedDate!.Value })
            .ToListAsync(ct);

        return [.. sessions, .. kept.Select(k => new HabitClip(k.SeriesId, k.Date, k.Date))];
    }

    /// <summary>
    /// Норма недели — «раз в неделю» цели без занятий «не нужно» (docs/specs/skips.md, H1); норма 0 — неделя без нормы
    /// (не засчитана и не пропуск), такой недели в списке нет.
    /// </summary>
    public async Task<List<HabitWeekNorm>> GetWeekNormsAsync(CancellationToken ct = default) =>
        (await db.WeeklyGoals.AsNoTracking()
            .Where(g => g.SeriesId != null && g.Series!.IsRecurring)
            .Select(g => new
            {
                SeriesId = g.SeriesId!.Value,
                g.WeekStart,
                Norm = g.TimesPerWeek - db.Set<TaskGoal>().Count(l => l.GoalId == g.Id
                    && l.Task.State == TaskState.Skipped && l.Task.SkipKind == SkipKind.NotNeeded),
            })
            .Where(x => x.Norm > 0)
            .ToListAsync(ct))
        .Select(x => new HabitWeekNorm(x.SeriesId, x.WeekStart, x.Norm))
        .ToList();
}

public class EfSettingsRepository(PlannerDbContext db) : ISettingsRepository
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await db.AppSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = AppSettings.CreateDefault();
            db.AppSettings.Add(settings);
        }

        return settings;
    }
}

public class EfTaskSkipRepository(PlannerDbContext db) : ITaskSkipRepository
{
    public Task<List<TaskSkip>> GetForTaskAsync(int taskId, CancellationToken ct = default) =>
        db.TaskSkips.Where(s => s.TaskId == taskId).OrderByDescending(s => s.RecordedAt).ThenByDescending(s => s.Id).ToListAsync(ct);

    public Task<List<TaskSkip>> GetInPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        db.TaskSkips.Where(s => s.Date >= from && s.Date <= to).OrderByDescending(s => s.Date).ThenByDescending(s => s.RecordedAt).ToListAsync(ct);

    public void Add(TaskSkip skip) => db.TaskSkips.Add(skip);

    public void Remove(TaskSkip skip) => db.TaskSkips.Remove(skip);
}

public class EfVacationRepository(PlannerDbContext db) : IVacationRepository
{
    public Task<Vacation?> FindAsync(int id, CancellationToken ct = default) =>
        db.Vacations.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<List<Vacation>> GetAllAsync(CancellationToken ct = default) =>
        db.Vacations.OrderByDescending(v => v.From).ThenByDescending(v => v.Id).ToListAsync(ct);

    public void Add(Vacation vacation) => db.Vacations.Add(vacation);

    public void Remove(Vacation vacation) => db.Vacations.Remove(vacation);
}
