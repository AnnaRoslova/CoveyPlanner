using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;

namespace Planner.Core.Services;

/// <summary>
/// Шаги дела в его окне (один уровень; дело с шагами — проект): добавить в конец, переименовать, отметить, порядок, удалить.
/// Шаг — полноценное дело (может стоять в дне, см. <see cref="TaskService"/>); новый шаг получает копию ролей родителя.
/// Отметили последний шаг — родитель отмечается сам (<see cref="ProjectService"/>).
/// </summary>
public class StepService(
    ITaskRepository tasks, ProjectService projects, GoalService goals, IUnitOfWork unitOfWork, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<List<TaskItem>> GetAsync(int parentId, CancellationToken ct = default)
    {
        await GetParentAsync(parentId, ct);
        return await tasks.GetStepsAsync(parentId, ct);
    }

    /// <summary>Новый шаг в конец. У занятия цели своих шагов нет — шаг уходит в проект его цели (G4; нет проекта — он создаётся, G3).</summary>
    public async Task<TaskItem> AddAsync(int parentId, string title, CancellationToken ct = default)
    {
        var parent = await GetParentAsync(parentId, ct);
        if (parent.GoalId is { } goalId)
        {
            return (await goals.AddStepAsync(goalId, title, ct)).Step;
        }

        var step = TaskItem.CreateStep(parent, title, Now);
        step.SetRoles(parent.RoleIds); // копия на момент создания, дальше независимы (S5)
        await projects.PlaceStepAsync(step, parentId, int.MaxValue, ct);
        tasks.Add(step);
        await unitOfWork.SaveChangesAsync(ct);
        return step;
    }

    public async Task<TaskItem> RenameAsync(int stepId, string title, CancellationToken ct = default)
    {
        var step = await GetStepAsync(stepId, ct);
        step.Rename(title);
        await unitOfWork.SaveChangesAsync(ct);
        return step;
    }

    /// <summary>Отметить шаг или снять отметку; родитель, если он отметился сам, — в Parent (S12).</summary>
    public async Task<TaskDone> SetDoneAsync(int stepId, bool isDone, CancellationToken ct = default)
    {
        var step = await GetStepAsync(stepId, ct);
        TaskItem? parent = null;
        if (isDone)
        {
            step.Complete(Now);
            parent = await projects.CompleteParentIfAllDoneAsync(step, Now, ct);
        }
        else
        {
            step.Uncomplete();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new TaskDone(step, parent);
    }

    /// <summary>Порядок шагов в списке родителя (все шаги, включая корзину, — в нужном порядке).</summary>
    public async Task ReorderAsync(int parentId, IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        await GetParentAsync(parentId, ct);
        var steps = await tasks.GetStepsAsync(parentId, ct);
        var byId = steps.ToDictionary(s => s.Id);
        if (orderedIds.Count != byId.Count || orderedIds.Distinct().Count() != orderedIds.Count
            || orderedIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new DomainException("Список для сортировки не совпадает с текущими шагами.");
        }

        for (var i = 0; i < orderedIds.Count; i++)
        {
            byId[orderedIds[i]].StepOrder = i;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int stepId, CancellationToken ct = default)
    {
        tasks.Remove(await GetStepAsync(stepId, ct));
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<TaskItem> GetParentAsync(int parentId, CancellationToken ct)
    {
        var parent = await tasks.FindAsync(parentId, ct) ?? throw new NotFoundException("Дело", parentId);
        return parent.IsStep ? throw new DomainException("У шага не бывает своих шагов.") : parent;
    }

    private async Task<TaskItem> GetStepAsync(int stepId, CancellationToken ct)
    {
        var step = await tasks.FindAsync(stepId, ct) ?? throw new NotFoundException("Шаг", stepId);
        return step.IsStep ? step : throw new DomainException("Это не шаг, а дело.");
    }
}
