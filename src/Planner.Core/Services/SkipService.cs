using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;

namespace Planner.Core.Services;

/// <summary>Дело после пропуска; родитель, если отметился сам (S1); ReturnedToProject — шаг «не сделала» вернулся в список проекта (S2).</summary>
public sealed record TaskSkipped(TaskItem Task, TaskItem? Parent, bool ReturnedToProject);

/// <summary>Пропуски дел: «не надо» и «не сделала» (docs/specs/skips.md, K, S).</summary>
public class SkipService(
    ITaskRepository tasks,
    ITaskSkipRepository skips,
    ProjectService projects,
    TaskPlacer placer,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Пропустить дело (уже пропущенное — сменить вид и причину). Запись — в историю (K2).</summary>
    public async Task<TaskSkipped> SkipAsync(int id, SkipKind kind, string? reason, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(id, ct) ?? throw new NotFoundException("Дело", id);
        if (task.IsSkipped)
        {
            await RemoveLatestAsync(task, ct);
            task.Unskip();
        }

        var date = task.PlannedDate ?? task.PlannedWeek;
        TaskItem? parent = null;
        var returned = false;

        // S2: шаг (не занятие цели) «не сделала» в дне — снимается с дня и возвращается в список проекта, причина — в истории
        if (kind == SkipKind.Missed && task is { IsStep: true, IsSession: false, PlannedDate: { } day, State: TaskState.Planned })
        {
            skips.Add(TaskSkip.Record(task, kind, reason, day, Now));
            task.PlanForWeek();
            returned = true;
        }
        else
        {
            task.Skip(kind, reason, Now); // правила K3 — здесь
            skips.Add(TaskSkip.Record(task, kind, task.SkipReason, date!.Value, Now));
            if (kind == SkipKind.NotNeeded && task is { IsStep: true, IsSession: false, ParentId: { } parentId })
            {
                parent = await projects.CompleteParentIfAllCountedDoneAsync(parentId, Now, ct); // S1: «не надо» не мешает автоотметке
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new TaskSkipped(task, parent, returned);
    }

    /// <summary>
    /// Отменить пропуск (K4): снова открыто; запись истории этого пропуска удаляется. returnTo — «Вернуть» после S2:
    /// шаг, ушедший в список проекта, — обратно в этот день (в конец дня), его запись «не сделала» удаляется.
    /// </summary>
    public async Task<TaskItem> UnskipAsync(int id, DateOnly? returnTo = null, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(id, ct) ?? throw new NotFoundException("Дело", id);
        if (returnTo is { } day && task is { IsSkipped: false, IsStep: true, PlannedDate: null }
            && (await skips.GetForTaskAsync(task.Id, ct)).FirstOrDefault() is { Kind: SkipKind.Missed } latest && latest.Date == day)
        {
            skips.Remove(latest);
            await placer.PlaceAsync(task, new DayTarget(day, int.MaxValue), ct);
            await unitOfWork.SaveChangesAsync(ct);
            return task;
        }

        if (task.IsSkipped)
        {
            await RemoveLatestAsync(task, ct);
            task.Unskip();
            await unitOfWork.SaveChangesAsync(ct);
        }

        return task;
    }

    public Task<List<TaskSkip>> HistoryAsync(int id, CancellationToken ct = default) => skips.GetForTaskAsync(id, ct);

    /// <summary>Пропуск снят (отменили или отметили сделанным) — его запись в истории больше не правда.</summary>
    public async Task RemoveLatestAsync(TaskItem task, CancellationToken ct = default)
    {
        if (task.IsSkipped && (await skips.GetForTaskAsync(task.Id, ct)).FirstOrDefault() is { } latest)
        {
            skips.Remove(latest);
        }
    }
}
