using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;

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

    /// <summary>Новое дело в ленте — наверх категории.</summary>
    public async Task<TaskItem> CreateInCategoryAsync(string title, int categoryId, CancellationToken ct = default)
    {
        var task = TaskItem.CreateSomeday(title, categoryId, Now);
        await placer.PlaceAsync(task, new SomedayTarget(categoryId, 0), ct);
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>Новое дело сразу в плане: в приоритеты дня или в сетку.</summary>
    public async Task<TaskItem> CreateInPlanAsync(string title, TaskTarget target, CancellationToken ct = default)
    {
        if (target is SomedayTarget someday)
        {
            return await CreateInCategoryAsync(title, someday.CategoryId, ct);
        }

        var task = TaskItem.CreatePlanned(title, Now);
        await placer.PlaceAsync(task, target, ct);
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    public async Task<TaskItem> UpdateAsync(
        int id, string title, string? notes, int? roleId, Quadrant? quadrant, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
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
        task.RoleId = roleId;
        task.Quadrant = quadrant;

        // Название у цели и связанного дела одно
        if (await goals.FindByTaskIdAsync(task.Id, ct) is { } goal)
        {
            goal.Rename(task.Title);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>Перетаскивание дела. При возврате в ленту связь с целью разрывается.</summary>
    public async Task<TaskItem> MoveAsync(int id, TaskTarget target, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        await placer.PlaceAsync(task, target, ct);

        if (target is SomedayTarget && await goals.FindByTaskIdAsync(task.Id, ct) is { } goal)
        {
            goal.UnlinkTask();
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

        if (await goals.FindByTaskIdAsync(task.Id, ct) is { } goal)
        {
            goal.IsDone = isDone;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>В корзину. Цель, если была связана, остаётся — просто без дела.</summary>
    public async Task TrashAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        task.Trash(Now);

        if (await goals.FindByTaskIdAsync(task.Id, ct) is { } goal)
        {
            goal.UnlinkTask();
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Вернуть из корзины на прежнее место. Если прежнее место потеряно
    /// (категория удалена или дело висело на цели без дня) — в ленту.
    /// </summary>
    public async Task<TaskItem> RestoreAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        task.Restore();

        if (task.State == TaskState.Someday || task.PlannedDate is null)
        {
            await placer.ReturnToSomedayAsync(task, ct);
        }
        else if (task.IsDayPriority)
        {
            var siblings = await tasks.GetDayPrioritiesAsync(task.PlannedDate.Value, ct);
            SortOrdering.Place(siblings, task, int.MaxValue);
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
