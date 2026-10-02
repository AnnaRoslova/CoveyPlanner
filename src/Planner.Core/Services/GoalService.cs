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
    IGoalSeriesRepository series,
    GoalSessions sessions,
    TaskPlacer placer,
    GoalRemover remover,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Новая цель — в конец списка целей роли на эту неделю. <paramref name="recurring"/> — «каждую неделю»;
    /// null — как принято у роли: у измерений пилы постоянная, у обычных ролей — разовая.
    /// </summary>
    public async Task<WeeklyGoal> CreateAsync(
        int roleId, DateOnly weekStart, string title, bool? recurring = null, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(roleId, ct);
        var goal = WeeklyGoal.Create(roleId, weekStart, title);
        var siblings = await goals.GetForRoleAsync(roleId, weekStart, ct);
        SortOrdering.Place(siblings, goal, int.MaxValue);
        goals.Add(goal);
        if (recurring ?? role.IsSharpenSaw)
        {
            StartRecurringSeries(goal);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>
    /// Сохранить окно цели: название, роль, «раз в неделю» и шаблон занятий (время, квадрант, заметки,
    /// длительность). Изменения шаблона расходятся по занятиям — см. <see cref="WeeklyGoal.UpdateTemplate"/>.
    /// Новая роль — цель встаёт в конец её списка, занятия получают эту роль.
    /// </summary>
    public async Task<GoalUpdate> UpdateAsync(int id, GoalChanges changes, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        if (changes.Quadrant is { } q && !Enum.IsDefined(q))
        {
            throw new DomainException("Квадрант должен быть от 1 до 4.");
        }

        var oldDays = goal.Days;
        if (changes.Days is { } days)
        {
            goal.SetDays(days);
        }

        goal.SetTimesPerWeek(changes.TimesPerWeek); // при выбранных днях «раз в неделю» — их число
        if (changes.AllowSeveralPerDay is { } several)
        {
            goal.SetAllowSeveralPerDay(several);
        }

        var lostTime = goal.UpdateTemplate(
            changes.Title, changes.StartTime, changes.Quadrant, changes.Notes, changes.DurationMinutes);
        foreach (var session in lostTime)
        {
            await placer.MoveToEndOfDayAsync(session, ct);
        }

        if (changes.RoleId != goal.RoleId)
        {
            await GetRoleAsync(changes.RoleId, ct);
            var siblings = await goals.GetForRoleAsync(changes.RoleId, goal.WeekStart, ct);
            goal.MoveToRole(changes.RoleId);
            SortOrdering.Place(siblings, goal, int.MaxValue);
        }

        // Дни поменялись — занятия этой недели раскладываются по новым дням (с этой недели и дальше)
        var removed = new List<TaskItem>();
        if (goal.Days != oldDays)
        {
            removed.AddRange(await sessions.ArrangeByDaysAsync(goal, ct));
        }

        await UpdateSeriesAsync(goal, changes.IsRecurring ?? goal.RepeatsAfterThisWeek, removed, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new GoalUpdate(goal, removed);
    }

    /// <summary>
    /// После правки цели — её серия. Постоянная и остаётся такой: шаблон серии и уже созданные цели следующих недель
    /// меняются так же (прошлые недели — нет). Выключили «каждую неделю» — серия заканчивается на этой неделе,
    /// уже созданные цели следующих недель удаляются. Включили — серия идёт с этой недели.
    /// </summary>
    /// <param name="removed">Куда сложить занятия, убранные в корзину при раскладке по новым дням.</param>
    private async Task UpdateSeriesAsync(WeeklyGoal goal, bool recurring, List<TaskItem> removed, CancellationToken ct)
    {
        var wasRecurring = goal.RepeatsAfterThisWeek;
        if (wasRecurring && !recurring)
        {
            goal.Series!.StopAfter(goal.WeekStart);
            foreach (var later in await LaterInSeriesAsync(goal, ct))
            {
                await remover.RemoveAsync(later, ct);
            }

            return;
        }

        if (!recurring)
        {
            return; // разовая цель — ничего, кроме неё самой, не меняется
        }

        if (goal.Series is null)
        {
            StartRecurringSeries(goal);
            return;
        }

        if (!wasRecurring)
        {
            goal.Series.RecurFrom(goal.WeekStart);
        }

        await SpreadToLaterWeeksAsync(goal, removed, ct);
    }

    /// <summary>
    /// Шаблон постоянной цели — в её серию и в уже созданные цели следующих недель
    /// (поменялись дни — занятия там раскладываются заново; убранные — в <paramref name="removed"/>).
    /// </summary>
    private async Task SpreadToLaterWeeksAsync(WeeklyGoal goal, List<TaskItem>? removed, CancellationToken ct)
    {
        goal.Series!.CopyTemplateFrom(goal);
        foreach (var later in await LaterInSeriesAsync(goal, ct))
        {
            var laterDays = later.Days;
            later.SetDays(goal.Days);
            later.SetTimesPerWeek(goal.TimesPerWeek);
            later.SetAllowSeveralPerDay(goal.AllowSeveralPerDay);
            var lostTime = later.UpdateTemplate(goal.Title, goal.StartTime, goal.Quadrant, goal.Notes, goal.DurationMinutes);
            foreach (var session in lostTime)
            {
                await placer.MoveToEndOfDayAsync(session, ct);
            }

            if (later.RoleId != goal.RoleId)
            {
                var siblings = await goals.GetForRoleAsync(goal.RoleId, later.WeekStart, ct);
                later.MoveToRole(goal.RoleId);
                SortOrdering.Place(siblings, later, int.MaxValue);
            }

            if (later.Days != laterDays)
            {
                var arranged = await sessions.ArrangeByDaysAsync(later, ct);
                removed?.AddRange(arranged);
            }
        }
    }

    /// <summary>Уже созданные цели той же серии на следующих неделях.</summary>
    private async Task<List<WeeklyGoal>> LaterInSeriesAsync(WeeklyGoal goal, CancellationToken ct) =>
        goal.SeriesId is { } seriesId
            ? await goals.GetInSeriesAsync(seriesId, goal.WeekStart.AddDays(Week.DaysInWeek), ct)
            : [];

    /// <summary>Сделать цель постоянной: новая серия «каждую неделю» с её шаблоном, начиная с её недели.</summary>
    private void StartRecurringSeries(WeeklyGoal goal)
    {
        var created = GoalSeries.StartFrom(goal, recurring: true);
        series.Add(created);
        goal.JoinSeries(created);
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
                ?? sessions.Create(goal);

            // Отметка без дня нигде не видна: на текущей неделе занятие встаёт в сегодня
            if (session.PlannedDate is null && isCurrentWeek)
            {
                // Не больше одного занятия цели в день: сегодня уже есть — новое не добавляем
                if (!goal.AllowSeveralPerDay && goal.HasSessionOn(today, session))
                {
                    throw new DomainException("Сегодня уже отмечено.");
                }

                await sessions.PlaceInDayAsync(goal, session, new DayTarget(today, int.MaxValue), ct);
            }

            session.Complete(Now);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return (goal, session);
    }

    /// <summary>
    /// Удалить цель; что станет с занятиями — см. <see cref="GoalRemover"/>. У постоянной цели:
    /// <paramref name="fromThisWeekOn"/> = false — только на этой неделе (на следующей она снова появится);
    /// true — серия заканчивается: цели с этой недели и дальше удаляются, прошлые недели остаются.
    /// </summary>
    public async Task DeleteAsync(int id, bool fromThisWeekOn = false, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        if (fromThisWeekOn && goal.Series is { IsRecurring: true } recurring)
        {
            recurring.StopAfter(goal.WeekStart.AddDays(-Week.DaysInWeek));
            foreach (var later in await LaterInSeriesAsync(goal, ct))
            {
                await remover.RemoveAsync(later, ct);
            }
        }

        await remover.RemoveAsync(goal, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Переставить цель внутри роли или перенести к другой роли (та же неделя).
    /// Постоянная цель переходит к другой роли и на следующих неделях.
    /// </summary>
    public async Task<WeeklyGoal> MoveAsync(int id, int roleId, int index, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        await GetRoleAsync(roleId, ct);

        var otherRole = goal.RoleId != roleId;
        var siblings = await goals.GetForRoleAsync(roleId, goal.WeekStart, ct);
        goal.MoveToRole(roleId);
        SortOrdering.Place(siblings, goal, index);
        if (otherRole && goal.RepeatsAfterThisWeek)
        {
            await SpreadToLaterWeeksAsync(goal, null, ct);
        }

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
        goal.EnsureDayIsFree(day.Date); // не больше одного занятия в день, если не разрешено несколько
        var session = goal.UnscheduledSession ?? sessions.Create(goal);

        await sessions.PlaceInDayAsync(goal, session, day, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return (goal, session);
    }

    /// <summary>
    /// Дело из ленты (или из плана) → цель роли на неделю; дело становится её занятием.
    /// Дело из ленты уходит из ленты и становится занятием без дня. У измерений пилы цель — постоянная.
    /// </summary>
    public async Task<(WeeklyGoal Goal, TaskItem Session)> CreateFromTaskAsync(
        int taskId, int roleId, DateOnly weekStart, int index, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(roleId, ct);
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
        if (role.IsSharpenSaw)
        {
            StartRecurringSeries(goal);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return (goal, task);
    }

    /// <summary>
    /// Цель → обратно в ленту: цель исчезает, в категорию (null — в «неразобранное») встаёт её занятие без дня (или новое дело).
    /// Остальные занятия — как при удалении цели.
    /// </summary>
    public async Task<TaskItem> MoveToSomedayAsync(int id, int? categoryId, int index, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        var task = goal.UnscheduledSession;
        if (task is null)
        {
            task = TaskItem.CreateSomeday(goal.Title, categoryId, Now);
            task.SetRoles([goal.RoleId]);
            tasks.Add(task);
        }
        else
        {
            goal.RemoveSession(task);
        }

        await placer.PlaceAsync(task, new SomedayTarget(categoryId, index), ct);
        await remover.RemoveAsync(goal, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    private async Task<WeeklyGoal> GetAsync(int id, CancellationToken ct) =>
        await goals.FindAsync(id, ct) ?? throw new NotFoundException("Цель", id);

    private async Task<Role> GetRoleAsync(int roleId, CancellationToken ct) =>
        await roles.FindAsync(roleId, ct) ?? throw new NotFoundException("Роль", roleId);
}
