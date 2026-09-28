using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>
/// Цели ролей на неделю и их связь с делами:
/// дело из ленты → цель; цель → день/час (создаёт или двигает дело); цель → обратно в ленту.
/// Репозиторий целей подгружает LinkedTask вместе с целью.
/// </summary>
public class GoalService(
    IGoalRepository goals,
    IRoleRepository roles,
    ITaskRepository tasks,
    TaskPlacer placer,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Новая цель — в конец списка целей роли на эту неделю.</summary>
    public async Task<WeeklyGoal> CreateAsync(int roleId, DateOnly weekStart, string title, CancellationToken ct = default)
    {
        await EnsureRoleExistsAsync(roleId, ct);
        var goal = WeeklyGoal.Create(roleId, weekStart, title);
        var siblings = await goals.GetForRoleAsync(roleId, weekStart, ct);
        SortOrdering.Place(siblings, goal, int.MaxValue);
        goals.Add(goal);
        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>Изменить название и/или отметку «выполнено» (синхронно со связанным делом).</summary>
    public async Task<WeeklyGoal> UpdateAsync(int id, string? title, bool? isDone, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        var task = goal.LinkedTask;

        if (title is not null)
        {
            goal.Rename(title);
            task?.Rename(goal.Title);
        }

        if (isDone is { } done)
        {
            goal.IsDone = done;
            if (task is not null)
            {
                if (done)
                {
                    task.Complete(Now);
                }
                else
                {
                    task.Uncomplete();
                }
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>
    /// Удалить цель. Дело без дня возвращается в ленту (без своей категории —
    /// в <paramref name="fallbackCategoryId"/>), дело в плане остаётся на месте.
    /// </summary>
    public async Task DeleteAsync(int id, int? fallbackCategoryId, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        if (goal.LinkedTask is { PlannedDate: null } task)
        {
            await placer.ReturnToSomedayAsync(task, fallbackCategoryId, ct);
        }

        goals.Remove(goal);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Переставить цель внутри роли или перенести к другой роли (та же неделя).</summary>
    public async Task<WeeklyGoal> MoveAsync(int id, int roleId, int index, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        await EnsureRoleExistsAsync(roleId, ct);

        var siblings = await goals.GetForRoleAsync(roleId, goal.WeekStart, ct);
        goal.MoveToRole(roleId);
        SortOrdering.Place(siblings, goal, index);

        if (goal.LinkedTask is { } task)
        {
            task.RoleId = roleId;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>Цель → день или час. Если дела ещё нет — создаётся с ролью цели.</summary>
    public async Task<WeeklyGoal> ScheduleAsync(int id, TaskTarget target, CancellationToken ct = default)
    {
        if (target is SomedayTarget)
        {
            throw new DomainException("Для возврата в ленту используйте перенос цели в ленту.");
        }

        var goal = await GetAsync(id, ct);
        var task = goal.LinkedTask;
        if (task is null)
        {
            task = TaskItem.CreatePlanned(goal.Title, Now);
            task.RoleId = goal.RoleId;
            tasks.Add(task);
            goal.LinkTask(task);
        }

        await placer.PlaceAsync(task, target, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>
    /// Дело из ленты (или из плана) → цель роли на неделю.
    /// Дело из ленты становится «запланированным на неделю» и уходит из ленты.
    /// </summary>
    public async Task<WeeklyGoal> CreateFromTaskAsync(
        int taskId, int roleId, DateOnly weekStart, int index, CancellationToken ct = default)
    {
        await EnsureRoleExistsAsync(roleId, ct);
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Дело", taskId);
        if (task.State == TaskState.Trashed)
        {
            throw new DomainException("Дело в корзине.");
        }

        if (await goals.FindByTaskIdAsync(taskId, ct) is not null)
        {
            throw new DomainException("Это дело уже связано с целью.");
        }

        var goal = WeeklyGoal.Create(roleId, weekStart, task.Title);
        if (task.State == TaskState.Someday)
        {
            task.PlanForWeek();
        }

        task.RoleId = roleId;
        goal.LinkTask(task);

        var siblings = await goals.GetForRoleAsync(roleId, weekStart, ct);
        SortOrdering.Place(siblings, goal, index);
        goals.Add(goal);

        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>Цель → обратно в ленту: цель исчезает, дело (существующее или новое) встаёт в категорию.</summary>
    public async Task<TaskItem> MoveToSomedayAsync(int id, int categoryId, int index, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        var task = goal.LinkedTask;
        if (task is null)
        {
            task = TaskItem.CreateSomeday(goal.Title, categoryId, Now);
            task.RoleId = goal.RoleId;
            tasks.Add(task);
        }

        await placer.PlaceAsync(task, new SomedayTarget(categoryId, index), ct);
        goals.Remove(goal);

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    private async Task<WeeklyGoal> GetAsync(int id, CancellationToken ct) =>
        await goals.FindAsync(id, ct) ?? throw new NotFoundException("Цель", id);

    private async Task EnsureRoleExistsAsync(int roleId, CancellationToken ct)
    {
        if (await roles.FindAsync(roleId, ct) is null)
        {
            throw new NotFoundException("Роль", roleId);
        }
    }
}
