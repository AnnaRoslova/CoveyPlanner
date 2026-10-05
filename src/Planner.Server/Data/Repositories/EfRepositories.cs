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

    public Task<List<TaskItem>> GetSomedayAsync(CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Someday)
            .OrderBy(t => t.CategoryId).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetSomedayInCategoryAsync(int? categoryId, CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Someday && t.CategoryId == categoryId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetUntimedOnDayAsync(DateOnly date, CancellationToken ct = default) =>
        db.Tasks.Where(t => (t.State == TaskState.Planned || t.State == TaskState.Done)
                            && t.PlannedDate == date && t.StartTime == null)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

    /// <summary>Порядок как на экране: сначала дела со временем (по времени), затем без времени (по SortOrder).</summary>
    public Task<List<TaskItem>> GetPlannedAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        db.Tasks.Where(t => (t.State == TaskState.Planned || t.State == TaskState.Done)
                            && t.PlannedDate >= from && t.PlannedDate <= to)
            .OrderBy(t => t.PlannedDate)
            .ThenBy(t => t.StartTime == null).ThenBy(t => t.StartTime)
            .ThenBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetTrashedAsync(CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Trashed)
            .OrderByDescending(t => t.TrashedAt)
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
        db.WeeklyGoals.Include(g => g.Links).ThenInclude(l => l.Task).Include(g => g.Series);

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

    public Task<List<WeeklyGoal>> GetInSeriesAsync(int seriesId, DateOnly fromWeek, CancellationToken ct = default) =>
        Goals.Where(g => g.SeriesId == seriesId && g.WeekStart >= fromWeek).OrderBy(g => g.WeekStart).ToListAsync(ct);

    public Task<WeeklyGoal?> FindInSeriesAsync(int seriesId, DateOnly weekStart, CancellationToken ct = default) =>
        Goals.FirstOrDefaultAsync(g => g.SeriesId == seriesId && g.WeekStart == weekStart, ct);

    public void Add(WeeklyGoal goal) => db.WeeklyGoals.Add(goal);

    public void Remove(WeeklyGoal goal) => db.WeeklyGoals.Remove(goal);
}

public class EfGoalSeriesRepository(PlannerDbContext db) : IGoalSeriesRepository
{
    public Task<List<GoalSeries>> GetRecurringAsync(CancellationToken ct = default) =>
        db.GoalSeries.Where(s => s.IsRecurring).ToListAsync(ct);

    public Task<List<GoalSeries>> GetWithRoleAsync(int roleId, CancellationToken ct = default) =>
        db.GoalSeries.Where(s => s.Roles.Any(r => r.RoleId == roleId)).ToListAsync(ct);

    public void Add(GoalSeries series) => db.GoalSeries.Add(series);

    public void Remove(GoalSeries series) => db.GoalSeries.Remove(series);
}

public class EfHabitRepository(PlannerDbContext db) : IHabitRepository
{
    public Task<Habit?> FindAsync(int id, CancellationToken ct = default) =>
        db.Habits.FirstOrDefaultAsync(h => h.Id == id, ct);

    public Task<List<Habit>> GetAllAsync(CancellationToken ct = default) =>
        db.Habits.OrderBy(h => h.SortOrder).ThenBy(h => h.Id).ToListAsync(ct);

    /// <summary>Один запрос: выполненные дела с копилкой; неделя цели — подзапросом по TaskGoals (для занятий без дня).</summary>
    public Task<List<HabitClipSource>> GetClipsAsync(CancellationToken ct = default) =>
        db.Tasks.AsNoTracking()
            .Where(t => t.HabitId != null && t.State == TaskState.Done)
            .Select(t => new HabitClipSource(
                t.HabitId!.Value,
                t.PlannedDate,
                t.GoalLinks.Select(l => (DateOnly?)l.Goal.WeekStart).FirstOrDefault(),
                t.CompletedAt))
            .ToListAsync(ct);

    public void Add(Habit habit) => db.Habits.Add(habit);

    public void Remove(Habit habit) => db.Habits.Remove(habit);
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
