using Microsoft.EntityFrameworkCore;
using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Server.Data.Repositories;

public class EfTaskRepository(PlannerDbContext db) : ITaskRepository
{
    public Task<TaskItem?> FindAsync(int id, CancellationToken ct = default) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<List<TaskItem>> GetSomedayAsync(CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Someday)
            .OrderBy(t => t.CategoryId).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetSomedayInCategoryAsync(int categoryId, CancellationToken ct = default) =>
        db.Tasks.Where(t => t.State == TaskState.Someday && t.CategoryId == categoryId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetDayPrioritiesAsync(DateOnly date, CancellationToken ct = default) =>
        db.Tasks.Where(t => (t.State == TaskState.Planned || t.State == TaskState.Done)
                            && t.PlannedDate == date && t.StartTime == null)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetPlannedAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        db.Tasks.Where(t => (t.State == TaskState.Planned || t.State == TaskState.Done)
                            && t.PlannedDate >= from && t.PlannedDate <= to)
            .OrderBy(t => t.PlannedDate).ThenBy(t => t.StartTime).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

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

    public async Task<Category> GetInboxAsync(CancellationToken ct = default) =>
        await db.Categories.FirstOrDefaultAsync(c => c.IsSystem, ct)
        ?? throw new InvalidOperationException("В базе нет категории «Входящие».");

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
    private IQueryable<WeeklyGoal> Goals => db.WeeklyGoals.Include(g => g.LinkedTask);

    public Task<WeeklyGoal?> FindAsync(int id, CancellationToken ct = default) =>
        Goals.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<WeeklyGoal?> FindByTaskIdAsync(int taskId, CancellationToken ct = default) =>
        Goals.FirstOrDefaultAsync(g => g.TaskId == taskId, ct);

    public Task<List<WeeklyGoal>> GetForWeekAsync(DateOnly weekStart, CancellationToken ct = default) =>
        Goals.Where(g => g.WeekStart == weekStart)
            .OrderBy(g => g.RoleId).ThenBy(g => g.SortOrder)
            .ToListAsync(ct);

    public Task<List<WeeklyGoal>> GetForRoleAsync(int roleId, DateOnly weekStart, CancellationToken ct = default) =>
        Goals.Where(g => g.RoleId == roleId && g.WeekStart == weekStart)
            .OrderBy(g => g.SortOrder)
            .ToListAsync(ct);

    public Task<List<WeeklyGoal>> GetAllForRoleAsync(int roleId, CancellationToken ct = default) =>
        Goals.Where(g => g.RoleId == roleId).ToListAsync(ct);

    public void Add(WeeklyGoal goal) => db.WeeklyGoals.Add(goal);

    public void Remove(WeeklyGoal goal) => db.WeeklyGoals.Remove(goal);
}

public class EfSettingsRepository(PlannerDbContext db) : ISettingsRepository
{
    public async Task<UserSettings> GetAsync(CancellationToken ct = default) =>
        await db.Settings.FirstOrDefaultAsync(s => s.Id == UserSettings.SingletonId, ct)
        ?? throw new InvalidOperationException("В базе нет строки настроек.");
}
