using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>Дело после отметки, родитель, если он отметился сам (все шаги сделаны), и занятие, отмеченное вместе с ним (R4).</summary>
public sealed record TaskDone(TaskItem Task, TaskItem? Parent, TaskItem? Session = null);

/// <summary>Сценарии работы с делами: создать, изменить, перенести, выполнить, корзина.</summary>
public class TaskService(
    ITaskRepository tasks,
    IRoleRepository roles,
    IGoalRepository goals,
    GoalSeriesService series,
    TaskPlacer placer,
    ProjectService projects,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Новое дело: в ленту (обычно наверх категории, Index = 0) или в день; isProject — сразу проект («+ проект», N6.11).</summary>
    public async Task<TaskItem> CreateAsync(string title, TaskTarget target, CancellationToken ct = default, bool isProject = false)
    {
        var task = target is SomedayTarget someday
            ? TaskItem.CreateSomeday(title, someday.CategoryId, Now)
            : TaskItem.CreatePlanned(title, Now);
        if (isProject)
        {
            task.MakeProject();
        }

        await placer.PlaceAsync(task, target, ct);
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// Изменить дело из окна редактирования. startTime / durationMinutes = null — убрать; roleIds пусто — без ролей.
    /// У занятия цели роли и квадрант всегда от цели — присланные значения не применяются.
    /// </summary>
    public async Task<TaskItem> UpdateAsync(
        int id, string title, string? notes, IReadOnlyCollection<int> roleIds, Quadrant? quadrant, TimeOnly? startTime,
        int? durationMinutes, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        // У занятия роли и квадрант — от цели (так же решает клиент)
        if (await GoalOfAsync(task, ct) is { } goal)
        {
            roleIds = goal.RoleIds;
            quadrant = goal.Quadrant;
        }

        foreach (var roleId in roleIds.Distinct())
        {
            if (await roles.FindAsync(roleId, ct) is null)
            {
                throw new NotFoundException("Роль", roleId);
            }
        }

        if (quadrant is { } q && !Enum.IsDefined(q))
        {
            throw new DomainException("Квадрант должен быть от 1 до 4.");
        }

        task.Rename(title);
        task.SetNotes(notes);
        task.SetDuration(durationMinutes);
        task.SetRoles(roleIds);
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

    /// <summary>
    /// Перенос дела (перетаскиванием или датой в окне). При возврате в ленту связь с целью разрывается.
    /// Занятие цели, перенесённое на другую неделю, остаётся «камнем»: становится занятием той же цели на той неделе
    /// (цели её серии, а если такой нет — новой копии цели, 1 раз в неделю); у прежней цели прогресс уменьшается.
    /// </summary>
    public async Task<TaskItem> MoveAsync(int id, TaskTarget target, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);

        // В список шагов (на строку родителя или в его раскрытый список) — снять с дня, остаться (стать) шагом (S20)
        if (target is StepListTarget stepList)
        {
            await projects.SetParentAsync(task, stepList.ParentId, ct);
            task.PlanForWeek();
            await projects.PlaceStepAsync(task, stepList.ParentId, stepList.Index, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return task;
        }

        // Занятие в день: его цель — на неделе того дня (эта же или та же на другой неделе); в день, где у неё
        // уже есть занятие, нельзя (если не разрешено несколько в день) — проверяем, пока ничего не сдвинули
        WeeklyGoal? transferTo = null;
        if (target is DayTarget day && await GoalOfAsync(task, ct) is { } goal)
        {
            var week = Week.StartOf(day.Date);
            var to = goal.WeekStart == week ? goal : await series.GoalForWeekAsync(goal, week, ct);
            if (task.PlannedDate != day.Date) // порядок внутри своего дня — можно и у старых дублей
            {
                to.EnsureDayIsFree(day.Date, except: task);
            }

            if (to != goal)
            {
                transferTo = to;
            }
        }

        await placer.PlaceAsync(task, target, ct);

        if (target is SomedayTarget)
        {
            task.DetachFromGoal();
        }

        // На другую неделю — занятие переходит к той же цели той недели
        transferTo?.AddSession(task);

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// Отметить или снять отметку. Отметили последний шаг проекта — родитель отмечается сам (S12, возвращается в Parent);
    /// сам родитель никогда не открывается (S13).
    /// </summary>
    /// <param name="viaSessionId">Шаг отмечают в строке или окне этого занятия цели: проект закончился — отмечается и занятие (R4).</param>
    public async Task<TaskDone> SetDoneAsync(int id, bool isDone, CancellationToken ct = default, int? viaSessionId = null)
    {
        var task = await GetAsync(id, ct);
        TaskItem? parent = null;
        TaskItem? session = null;
        if (isDone)
        {
            task.Complete(Now);
            parent = await projects.CompleteParentIfAllDoneAsync(task, Now, ct);
            // R4: из занятия — после отметки все шаги проекта сделаны (даже если сам проект отметили раньше) — и занятие
            if (viaSessionId is { } sessionId && task.ParentId is { } projectId && await projects.AllStepsDoneAsync(projectId, ct))
            {
                session = await CompleteSessionOfProjectAsync(sessionId, projectId, ct);
            }
        }
        else
        {
            task.Uncomplete();
            if (task.State == TaskState.Someday)
            {
                await placer.ReturnToOwnPlaceAsync(task, ct); // выполненное в ленте — обратно на своё место (D6)
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new TaskDone(task, parent, session);
    }

    /// <summary>R4: занятие, из которого отметили последний шаг, — отметить, если оно занятие цели с этим проектом и ещё не отмечено.</summary>
    private async Task<TaskItem?> CompleteSessionOfProjectAsync(int sessionId, int projectId, CancellationToken ct)
    {
        if (await tasks.FindAsync(sessionId, ct) is not { State: TaskState.Planned, GoalId: { } goalId } session
            || await goals.FindAsync(goalId, ct) is not { } goal || goal.ProjectId != projectId)
        {
            return null;
        }

        session.Complete(Now);
        return session;
    }

    /// <summary>
    /// «Проект» в карточке: сделать дело шагом <paramref name="parentId"/> или убрать из проекта (null). См. <see cref="ProjectService.SetParentAsync"/>.
    /// </summary>
    public async Task<TaskItem> SetParentAsync(int id, int? parentId, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        await projects.SetParentAsync(task, parentId, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// «🛠 Сделать проектом» / «🛠 больше не проект» (N6.3, N6.7). Перестать быть проектом можно только без шагов (корзина не в счёт);
    /// проект без дня тогда — снова мысль ленты (уже в ленте — на своём месте; из плана без дня — в свою категорию).
    /// </summary>
    public async Task<TaskItem> SetProjectAsync(int id, bool isProject, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        if (isProject)
        {
            task.MakeProject();
        }
        else if (task.IsProject)
        {
            if ((await tasks.GetStepsAsync(task.Id, ct)).Any(s => s.State != TaskState.Trashed))
            {
                throw new DomainException(ProjectHasStepsMessage);
            }

            task.UnmakeProject();
            if (task is { State: TaskState.Planned, PlannedDate: null, IsSession: false })
            {
                await placer.ReturnToSomedayAsync(task, ct);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    public const string ProjectHasStepsMessage = "У проекта есть шаги — сначала уберите их.";

    /// <summary>
    /// В корзину. Занятие цели остаётся связанным с ней, но в прогресс не входит; вернули из корзины — снова засчитывается.
    /// Проект уходит вместе со своими шагами (занятия среди них отвязываются и остаются) — возвращаются ушедшие с ним шаги.
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>> TrashAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        var steps = await projects.TrashAsync(task, Now, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return steps;
    }

    /// <summary>
    /// Вернуть из корзины на прежнее место (см. <see cref="ProjectService.RestoreAsync"/>). Занятие цели без дня снова висит на цели.
    /// Дело ленты возвращается в свою категорию, а если её нет — в «неразобранное».
    /// </summary>
    public async Task<TaskItem> RestoreAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        await projects.RestoreAsync(task, ct);

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

    /// <summary>Цель занятия (загруженная — через неё видны её занятия); null — дело не «камень».</summary>
    private async Task<WeeklyGoal?> GoalOfAsync(TaskItem task, CancellationToken ct) =>
        task.GoalId is { } goalId ? await goals.FindAsync(goalId, ct) : null;

    private async Task<TaskItem> GetAsync(int id, CancellationToken ct) =>
        await tasks.FindAsync(id, ct) ?? throw new NotFoundException("Дело", id);
}
