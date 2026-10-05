using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>
/// Шаги дела (дело-чеклист, один уровень): добавить в конец, переименовать, отметить, удалить. Шаг видно только в окне
/// его дела; удаляется сразу (без корзины). Пока без засчитывания шага в цель и без автоотметки дела — это этап 4.
/// </summary>
public class StepService(ITaskRepository tasks, IUnitOfWork unitOfWork, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<List<TaskItem>> GetAsync(int parentId, CancellationToken ct = default)
    {
        await GetParentAsync(parentId, ct);
        return await tasks.GetStepsAsync(parentId, ct);
    }

    public async Task<TaskItem> AddAsync(int parentId, string title, CancellationToken ct = default)
    {
        var parent = await GetParentAsync(parentId, ct);
        var step = TaskItem.CreateStep(parent, title, Now);
        SortOrdering.Place(await tasks.GetStepsAsync(parentId, ct), step, int.MaxValue);
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

    public async Task<TaskItem> SetDoneAsync(int stepId, bool isDone, CancellationToken ct = default)
    {
        var step = await GetStepAsync(stepId, ct);
        if (isDone)
        {
            step.Complete(Now);
        }
        else
        {
            step.Uncomplete();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return step;
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
