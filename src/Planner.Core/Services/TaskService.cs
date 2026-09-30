using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;

namespace Planner.Core.Services;

/// <summary>Сценарии работы с делами: создать, изменить, перенести, выполнить, корзина.</summary>
public class TaskService(
    ITaskRepository tasks,
    IRoleRepository roles,
    IGoalRepository goals,
    TaskPlacer placer,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Новое дело: в ленту (обычно наверх категории, Index = 0) или в день.</summary>
    public async Task<TaskItem> CreateAsync(string title, TaskTarget target, CancellationToken ct = default)
    {
        var task = target is SomedayTarget someday
            ? TaskItem.CreateSomeday(title, someday.CategoryId, Now)
            : TaskItem.CreatePlanned(title, Now);
        await placer.PlaceAsync(task, target, ct);
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// Изменить дело из окна редактирования. startTime / durationMinutes = null — убрать.
    /// У занятия цели роль и квадрант всегда от цели — присланные значения не применяются.
    /// </summary>
    public async Task<TaskItem> UpdateAsync(
        int id, string title, string? notes, int? roleId, Quadrant? quadrant, TimeOnly? startTime,
        int? durationMinutes, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        if (task.GoalId is { } goalId && await goals.FindAsync(goalId, ct) is { } goal)
        {
            roleId = goal.RoleId;
            quadrant = goal.Quadrant;
        }

        if (roleId is { } rid && await roles.FindAsync(rid, ct) is null)
        {
            throw new NotFoundException("Роль", rid);
        }

        if (quadrant is { } q && !Enum.IsDefined(q))
        {
            throw new DomainException("Квадрант должен быть от 1 до 4.");
        }

        task.Rename(title);
        task.SetNotes(notes);
        task.SetDuration(durationMinutes);
        task.RoleId = roleId;
        task.Quadrant = quadrant;

        var hadTime = task.StartTime is not null;
        task.SetTime(startTime);
        if (hadTime && startTime is null)
        {
            // Время убрали — дело встаёт в конец ручного списка дня
            await placer.MoveToEndOfDayAsync(task, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>Перетаскивание дела. При возврате в ленту связь с целью разрывается.</summary>
    public async Task<TaskItem> MoveAsync(int id, TaskTarget target, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        await placer.PlaceAsync(task, target, ct);

        if (target is SomedayTarget)
        {
            task.DetachFromGoal();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    public async Task<TaskItem> SetDoneAsync(int id, bool isDone, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        if (isDone)
        {
            task.Complete(Now);
        }
        else
        {
            task.Uncomplete();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// В корзину. Занятие цели остаётся связанным с ней, но в прогресс не входит;
    /// вернули из корзины — снова засчитывается.
    /// </summary>
    public async Task TrashAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        task.Trash(Now);

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Вернуть из корзины на прежнее место. Занятие цели без дня снова висит на цели.
    /// Если дело возвращается в ленту (было в ленте или его цель удалили), а его категории нет —
    /// нужна <paramref name="fallbackCategoryId"/>.
    /// </summary>
    public async Task<TaskItem> RestoreAsync(int id, int? fallbackCategoryId, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        task.Restore();

        if (task.IsOnDay)
        {
            await placer.MoveToEndOfDayAsync(task, ct);
        }
        else if (task.GoalId is null)
        {
            await placer.ReturnToSomedayAsync(task, fallbackCategoryId, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    public async Task DeleteForeverAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        if (task.State != TaskState.Trashed)
        {
            throw new DomainException("Удалить навсегда можно только дело из корзины.");
        }

        tasks.Remove(task);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task EmptyTrashAsync(CancellationToken ct = default)
    {
        foreach (var task in await tasks.GetTrashedAsync(ct))
        {
            tasks.Remove(task);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<TaskItem> GetAsync(int id, CancellationToken ct) =>
        await tasks.FindAsync(id, ct) ?? throw new NotFoundException("Дело", id);
}
