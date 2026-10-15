using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;

namespace Planner.Core.Services;

/// <summary>
/// Сводка проекта (дела с шагами): «☑ StepsDone/StepsTotal» — шаги в счёте (не занятия целей, не корзина),
/// SessionsDone — выполненные занятия целей среди шагов («12 занятий»).
/// </summary>
public sealed record ProjectSummary(int StepsDone, int StepsTotal, int SessionsDone);

/// <summary>
/// Правила проекта — чистые функции (docs/specs/tasks-steps-done.md, S12–S17). Занятия целей среди шагов
/// в счёт и автоотметку не входят: книга с занятиями «Чтения» сама не отмечается.
/// </summary>
public static class ProjectRules
{
    /// <summary>Шаг в счёте проекта: не в корзине и не занятие цели.</summary>
    public static bool CountsAsStep(TaskItem child) => child.State != TaskState.Trashed && !child.IsSession;

    /// <summary>Сводка по шагам дела; null — шагов нет (не считая корзины).</summary>
    public static ProjectSummary? Summarize(IEnumerable<TaskItem> children)
    {
        var alive = children.Where(c => c.State != TaskState.Trashed).ToList();
        if (alive.Count == 0)
        {
            return null;
        }

        var steps = alive.Where(c => !c.IsSession).ToList();
        return new ProjectSummary(
            steps.Count(s => s.State == TaskState.Done),
            steps.Count,
            alive.Count(c => c.IsSession && c.State == TaskState.Done));
    }

    /// <summary>
    /// S12: отметили шаг, и все шаги в счёте (их хотя бы один) выполнены, а родитель — нет: родитель отмечается сам.
    /// Обратного нет (S13): родитель сам никогда не открывается.
    /// </summary>
    public static bool ShouldAutoComplete(TaskItem parent, IEnumerable<TaskItem> children)
    {
        if (parent.State is TaskState.Done or TaskState.Trashed)
        {
            return false;
        }

        var steps = children.Where(CountsAsStep).ToList();
        return steps.Count > 0 && steps.All(s => s.State == TaskState.Done);
    }
}

/// <summary>
/// Проекты и шаги: стать шагом и перестать им быть, место в списке шагов, автоотметка родителя,
/// корзина проекта целиком. Общий помощник для сервисов дел, шагов, целей и категорий. Сам не сохраняет изменения.
/// </summary>
public class ProjectService(ITaskRepository tasks, TaskPlacer placer)
{
    // Шаги, поставленные в списки в этом запросе и ещё не сохранённые: запрос к базе их не видит
    private readonly List<TaskItem> _pending = [];

    /// <summary>
    /// «Проект» в карточке (S6–S10): сделать дело шагом <paramref name="parentId"/> (в конец списка) или убрать из проекта (null).
    /// Дело с шагами шагом стать не может (один уровень). Убранный из проекта шаг без дня (не занятие) уходит в ленту —
    /// в свою категорию или в «неразобранное».
    /// </summary>
    public async Task SetParentAsync(TaskItem task, int? parentId, CancellationToken ct = default)
    {
        if (task.ParentId == parentId)
        {
            return;
        }

        if (parentId is not { } id)
        {
            task.SetParent(null);
            if (task is { State: TaskState.Planned, PlannedDate: null, PlannedWeek: null, IsSession: false })
            {
                await placer.ReturnToSomedayAsync(task, ct);
            }

            return;
        }

        var parent = await tasks.FindAsync(id, ct) ?? throw new NotFoundException("Дело", id);
        if (parent.IsSession)
        {
            throw new DomainException(SessionNotProjectMessage); // S7, G4
        }

        if ((await tasks.GetStepsAsync(task.Id, ct)).Count > 0)
        {
            throw new DomainException(HasStepsMessage);
        }

        task.SetParent(parent);
        await PlaceStepAsync(task, id, int.MaxValue, ct);
    }

    public const string HasStepsMessage = "У дела есть шаги — оно само проект.";

    public const string SessionNotProjectMessage = "Занятие цели не бывает проектом — шаги цели живут в её проекте.";

    /// <summary>Поставить шаг на место <paramref name="index"/> в списке шагов родителя (индекс — без самого шага).</summary>
    public async Task PlaceStepAsync(TaskItem step, int parentId, int index, CancellationToken ct = default)
    {
        var siblings = (await tasks.GetStepsAsync(parentId, ct))
            .Concat(_pending.Where(p => p.ParentId == parentId))
            .Where(s => s != step)
            .Distinct()
            .OrderBy(s => s.StepOrder ?? int.MaxValue)
            .ToList();
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), step);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].StepOrder = i;
        }

        if (!_pending.Contains(step))
        {
            _pending.Add(step);
        }
    }

    /// <summary>
    /// S12: после отметки шага — если все шаги проекта в счёте выполнены, родитель отмечается сам.
    /// Возвращает родителя, если его отметили.
    /// </summary>
    public async Task<TaskItem?> CompleteParentIfAllDoneAsync(TaskItem step, DateTime now, CancellationToken ct = default)
    {
        if (step is not { ParentId: { } parentId, IsSession: false, State: TaskState.Done }
            || await tasks.FindAsync(parentId, ct) is not { } parent)
        {
            return null;
        }

        if (!ProjectRules.ShouldAutoComplete(parent, await tasks.GetStepsAsync(parentId, ct)))
        {
            return null;
        }

        parent.Complete(now);
        return parent;
    }

    /// <summary>Все шаги проекта в счёте (не занятия, не корзина) выполнены, и они есть (для R4 — отметить и занятие).</summary>
    public async Task<bool> AllStepsDoneAsync(int projectId, CancellationToken ct = default)
    {
        var steps = (await tasks.GetStepsAsync(projectId, ct)).Where(ProjectRules.CountsAsStep).ToList();
        return steps.Count > 0 && steps.All(s => s.State == TaskState.Done);
    }

    /// <summary>
    /// В корзину (S23). У проекта вместе с ним уходят его шаги в счёте — где бы они ни стояли (тот же TrashedAt — по нему
    /// они и возвращаются вместе); занятия целей среди шагов отвязываются и остаются в своих днях и целях.
    /// Возвращает шаги, ушедшие в корзину вместе с делом.
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>> TrashAsync(TaskItem task, DateTime now, CancellationToken ct = default)
    {
        var trashedSteps = new List<TaskItem>();
        if (!task.IsStep)
        {
            foreach (var step in await tasks.GetStepsAsync(task.Id, ct))
            {
                if (step.State == TaskState.Trashed)
                {
                    continue; // удалён раньше отдельно — там и остаётся
                }

                if (step.IsSession)
                {
                    step.SetParent(null); // скрепки привычки не теряются
                    continue;
                }

                step.Trash(now);
                trashedSteps.Add(step);
            }
        }

        task.Trash(now);
        return trashedSteps;
    }

    /// <summary>
    /// Вернуть из корзины (S24, S26). Проект возвращается со своими шагами, ушедшими в корзину вместе с ним;
    /// шаг, чей родитель в корзине, возвращает и родителя (только его). Отвязанные занятия обратно не привязываются.
    /// </summary>
    public async Task RestoreAsync(TaskItem task, CancellationToken ct = default)
    {
        if (task.ParentId is { } parentId && await tasks.FindAsync(parentId, ct) is { } parent)
        {
            if (parent.State == TaskState.Trashed)
            {
                parent.Restore();
                await PlaceRestoredAsync(parent, ct);
            }

            parent.MarkAsProject(); // шаг вернулся — у дела снова есть шаг (N6.6)
        }

        var trashedAt = task.TrashedAt;
        task.Restore();
        await PlaceRestoredAsync(task, ct);

        if (!task.IsStep)
        {
            foreach (var step in await tasks.GetStepsAsync(task.Id, ct))
            {
                if (step.State == TaskState.Trashed && step.TrashedAt == trashedAt)
                {
                    step.Restore();
                    await PlaceRestoredAsync(step, ct);
                }
            }
        }
    }

    /// <summary>
    /// Место вернувшегося из корзины: в дне — в конец дня; в ленте — в свою категорию или в «неразобранное».
    /// Шаг без дня — на своё место в списке, выполненное в ленте — в «Выполненное», занятие без дня — на цель.
    /// </summary>
    private async Task PlaceRestoredAsync(TaskItem task, CancellationToken ct)
    {
        if (task.IsOnDay)
        {
            await placer.MoveToEndOfDayAsync(task, ct);
        }
        else if (task.State == TaskState.Someday
                 || task is { State: TaskState.Planned, PlannedDate: null, PlannedWeek: null, IsSession: false, IsStep: false })
        {
            await placer.ReturnToSomedayAsync(task, ct);
        }
    }
}
