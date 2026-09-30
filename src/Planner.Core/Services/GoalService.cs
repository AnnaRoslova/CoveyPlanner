using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>
/// Цели ролей на неделю и их занятия:
/// дело из ленты → цель; цель → день (новое занятие); отметка цели; цель → обратно в ленту.
/// Репозиторий целей подгружает занятия (Sessions) вместе с целью.
/// </summary>
public class GoalService(
    IGoalRepository goals,
    IRoleRepository roles,
    ITaskRepository tasks,
    TaskPlacer placer,
    GoalRemover remover,
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

    /// <summary>
    /// Сохранить окно цели: название, роль, «раз в неделю» и шаблон занятий (время, квадрант, заметки,
    /// длительность). Изменения шаблона расходятся по занятиям — см. <see cref="WeeklyGoal.UpdateTemplate"/>.
    /// Новая роль — цель встаёт в конец её списка, занятия получают эту роль.
    /// </summary>
    public async Task<WeeklyGoal> UpdateAsync(int id, GoalChanges changes, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        if (changes.Quadrant is { } q && !Enum.IsDefined(q))
        {
            throw new DomainException("Квадрант должен быть от 1 до 4.");
        }

        goal.SetTimesPerWeek(changes.TimesPerWeek);
        var lostTime = goal.UpdateTemplate(
            changes.Title, changes.StartTime, changes.Quadrant, changes.Notes, changes.DurationMinutes);
        foreach (var session in lostTime)
        {
            await placer.MoveToEndOfDayAsync(session, ct);
        }

        if (changes.RoleId != goal.RoleId)
        {
            await EnsureRoleExistsAsync(changes.RoleId, ct);
            var siblings = await goals.GetForRoleAsync(changes.RoleId, goal.WeekStart, ct);
            goal.MoveToRole(changes.RoleId);
            SortOrdering.Place(siblings, goal, int.MaxValue);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>
    /// Клик по отметке цели. Цель не выполнена — отмечается ближайшее невыполненное занятие
    /// (сегодняшнее, иначе самое раннее, иначе без дня); если таких нет — добавляется выполненное занятие
    /// на <paramref name="today"/> (для другой недели — без дня). Цель выполнена — снимается отметка
    /// с последнего отмеченного занятия. Возвращает цель и занятие, которое поменялось.
    /// </summary>
    public async Task<(WeeklyGoal Goal, TaskItem Session)> ToggleDoneAsync(int id, DateOnly today, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        var isCurrentWeek = Week.StartOf(today) == goal.WeekStart;
        TaskItem session;

        if (goal.IsDone)
        {
            session = goal.ActiveSessions
                .Where(t => t.State == TaskState.Done)
                .OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id)
                .First();
            session.Uncomplete();
        }
        else
        {
            var open = goal.ActiveSessions.Where(t => t.State == TaskState.Planned).ToList();
            session = open.FirstOrDefault(t => t.PlannedDate == today)
                ?? open.Where(t => t.PlannedDate is not null)
                    .OrderBy(t => t.PlannedDate).ThenBy(t => t.StartTime ?? TimeOnly.MaxValue).ThenBy(t => t.SortOrder)
                    .FirstOrDefault()
                ?? goal.UnscheduledSession
                ?? NewSession(goal);

            // Отметка без дня нигде не видна: на текущей неделе занятие встаёт в сегодня
            if (session.PlannedDate is null && isCurrentWeek)
            {
                await PlaceInDayAsync(goal, session, new DayTarget(today, int.MaxValue), ct);
            }

            session.Complete(Now);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return (goal, session);
    }

    /// <summary>Удалить цель; что станет с занятиями — см. <see cref="GoalRemover"/>.</summary>
    public async Task DeleteAsync(int id, int? fallbackCategoryId, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        await remover.RemoveAsync(goal, fallbackCategoryId, ct);
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

        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>
    /// Цель → день: новое занятие по шаблону цели (название, роль, время, квадрант, заметки, длительность). Если есть занятие без дня
    /// (цель сделана из дела ленты) — в день встаёт оно. Перенос занятия — перетаскиванием самого дела.
    /// </summary>
    public async Task<(WeeklyGoal Goal, TaskItem Session)> ScheduleAsync(int id, TaskTarget target, CancellationToken ct = default)
    {
        if (target is not DayTarget day)
        {
            throw new DomainException("Для возврата в ленту используйте перенос цели в ленту.");
        }

        var goal = await GetAsync(id, ct);
        var session = goal.UnscheduledSession ?? NewSession(goal);

        await PlaceInDayAsync(goal, session, day, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return (goal, session);
    }

    /// <summary>
    /// Дело из ленты (или из плана) → цель роли на неделю; дело становится её занятием.
    /// Дело из ленты уходит из ленты и становится занятием без дня.
    /// </summary>
    public async Task<(WeeklyGoal Goal, TaskItem Session)> CreateFromTaskAsync(
        int taskId, int roleId, DateOnly weekStart, int index, CancellationToken ct = default)
    {
        await EnsureRoleExistsAsync(roleId, ct);
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Дело", taskId);
        if (task.State == TaskState.Trashed)
        {
            throw new DomainException("Дело в корзине.");
        }

        if (task.GoalId is not null)
        {
            throw new DomainException("Это дело уже связано с целью.");
        }

        var goal = WeeklyGoal.Create(roleId, weekStart, task.Title);
        goal.CopyTemplateFrom(task);
        if (task.State == TaskState.Someday)
        {
            task.PlanForWeek();
        }

        goal.AddSession(task);

        var siblings = await goals.GetForRoleAsync(roleId, weekStart, ct);
        SortOrdering.Place(siblings, goal, index);
        goals.Add(goal);

        await unitOfWork.SaveChangesAsync(ct);
        return (goal, task);
    }

    /// <summary>
    /// Цель → обратно в ленту: цель исчезает, в категорию встаёт её занятие без дня (или новое дело).
    /// Остальные занятия — как при удалении цели.
    /// </summary>
    public async Task<TaskItem> MoveToSomedayAsync(int id, int categoryId, int index, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        var task = goal.UnscheduledSession;
        if (task is null)
        {
            task = TaskItem.CreateSomeday(goal.Title, categoryId, Now);
            task.RoleId = goal.RoleId;
            tasks.Add(task);
        }
        else
        {
            goal.RemoveSession(task);
        }

        await placer.PlaceAsync(task, new SomedayTarget(categoryId, index), ct);
        await remover.RemoveAsync(goal, fallbackCategoryId: categoryId, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    private TaskItem NewSession(WeeklyGoal goal)
    {
        var session = TaskItem.CreatePlanned(goal.Title, Now);
        goal.FillFromTemplate(session);
        tasks.Add(session);
        goal.AddSession(session);
        return session;
    }

    /// <summary>Занятие → день; без своего времени оно берёт время цели.</summary>
    private async Task PlaceInDayAsync(WeeklyGoal goal, TaskItem session, DayTarget day, CancellationToken ct)
    {
        await placer.PlaceAsync(session, day, ct);
        if (session.StartTime is null && goal.StartTime is not null)
        {
            session.SetTime(goal.StartTime);
        }
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
