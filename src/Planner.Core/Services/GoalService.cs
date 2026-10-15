using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
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
    GoalOrdering ordering,
    ProjectService projects,
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
        await ordering.PlaceAsync(goal, roleId, int.MaxValue, ct: ct);
        goals.Add(goal);
        if (recurring ?? role.IsSharpenSaw)
        {
            await StartRecurringSeriesAsync(goal, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return goal;
    }

    /// <summary>
    /// Сохранить окно цели: название, роли, «раз в неделю» и шаблон занятий (время, квадрант, заметки,
    /// длительность). Изменения шаблона расходятся по занятиям — см. <see cref="WeeklyGoal.UpdateTemplate"/>.
    /// Новая роль — цель встаёт в конец её списка; роли занятий — роли цели.
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

        if (changes.ProjectId is { } projectId)
        {
            goal.SetProject(projectId == 0 ? null : await GetProjectAsync(goal, projectId, ct));
        }

        var lostTime = goal.UpdateTemplate(
            changes.Title, changes.StartTime, changes.Quadrant, changes.Notes, changes.DurationMinutes);
        foreach (var session in lostTime)
        {
            await placer.MoveToEndOfDayAsync(session, ct);
        }

        if (!changes.RoleIds.Distinct().Order().SequenceEqual(goal.RoleIds))
        {
            foreach (var roleId in changes.RoleIds)
            {
                await GetRoleAsync(roleId, ct);
            }

            await ordering.PlaceAtEndAsync(goal, goal.SetRoleIds(changes.RoleIds), ct: ct);
        }

        // Дни поменялись — занятия этой недели раскладываются по новым дням (с этой недели и дальше)
        var removed = new List<TaskItem>();
        if (goal.Days != oldDays)
        {
            removed.AddRange(await sessions.ArrangeByDaysAsync(goal, ct));
        }

        await UpdateSeriesAsync(goal, changes.IsRecurring ?? goal.RepeatsAfterThisWeek, changes.IsHabit, removed, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new GoalUpdate(goal, removed);
    }

    /// <summary>
    /// После правки цели — её серия. Постоянная и остаётся такой: шаблон серии и уже созданные цели следующих недель
    /// меняются так же (прошлые недели — нет). Выключили «каждую неделю» — серия заканчивается на этой неделе,
    /// уже созданные цели следующих недель удаляются. Включили — серия идёт с этой недели.
    /// </summary>
    /// <param name="isHabit">«🫙 копить» из окна; null — не менять (а когда «каждую неделю» включают — по умолчанию).</param>
    /// <param name="removed">Куда сложить занятия, убранные в корзину при раскладке по новым дням.</param>
    private async Task UpdateSeriesAsync(
        WeeklyGoal goal, bool recurring, bool? isHabit, List<TaskItem> removed, CancellationToken ct)
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
            await StartRecurringSeriesAsync(goal, ct, isHabit);
            return;
        }

        if (!wasRecurring)
        {
            goal.Series.RecurFrom(goal.WeekStart);
            goal.Series.SetHabit(isHabit ?? await HabitByDefaultAsync(goal, ct));
        }
        else if (isHabit is { } habit)
        {
            goal.Series.SetHabit(habit); // свойство серии целиком, не «с этой недели»
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
            later.SetProject(goal.Project);
            var lostTime = later.UpdateTemplate(goal.Title, goal.StartTime, goal.Quadrant, goal.Notes, goal.DurationMinutes);
            foreach (var session in lostTime)
            {
                await placer.MoveToEndOfDayAsync(session, ct);
            }

            await ordering.PlaceAtEndAsync(later, later.SetRoleIds(goal.RoleIds), ct: ct);

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

    /// <summary>
    /// Сделать цель постоянной: новая серия «каждую неделю» с её шаблоном, начиная с её недели;
    /// «🫙 копить» — <paramref name="isHabit"/>, а если не задано — по умолчанию (<see cref="HabitByDefaultAsync"/>).
    /// </summary>
    private async Task StartRecurringSeriesAsync(WeeklyGoal goal, CancellationToken ct, bool? isHabit = null)
    {
        var created = GoalSeries.StartFrom(goal, recurring: true);
        created.SetHabit(isHabit ?? await HabitByDefaultAsync(goal, ct));
        series.Add(created);
        goal.JoinSeries(created);
    }

    /// <summary>
    /// Проект новых занятий цели (P1, P9): дело без родителя, не в корзине и не занятие этой же цели. Выполненный можно —
    /// тогда новые занятия просто создаются без проекта (P3).
    /// </summary>
    private async Task<TaskItem> GetProjectAsync(WeeklyGoal goal, int projectId, CancellationToken ct)
    {
        var project = await tasks.FindAsync(projectId, ct) ?? throw new NotFoundException("Дело", projectId);
        if (project.IsStep)
        {
            throw new DomainException("Проектом может быть только дело, а не шаг.");
        }

        if (project.State == TaskState.Trashed)
        {
            throw new DomainException("Проект в корзине — сначала верните его.");
        }

        if (project.IsSession)
        {
            throw new DomainException(ProjectService.SessionNotProjectMessage);
        }

        project.MakeProject(); // выбран проектом цели — её занятия и шаги будут его шагами (N6.6)
        return project;
    }

    /// <summary>
    /// «+ шаг» цели (G1–G4): шаг — в конец списка проекта цели. Проекта нет или он в корзине — сначала проект (G3, G9): дело с названием
    /// и ролями цели наверху «неразобранного», выбранное в цели (у постоянной — с этой недели и дальше, как выбор в поле «Проект»).
    /// Выполненный проект остаётся выполненным (G8, S13). Возвращает цель, шаг и проект, если его создали сейчас.
    /// </summary>
    public async Task<GoalStep> AddStepAsync(int id, string title, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        TaskItem? created = null;
        if (goal.Project is not { State: not TaskState.Trashed } project)
        {
            project = TaskItem.CreateSomeday(goal.Title, null, Now);
            project.MakeProject(); // сразу в «🛠 Проекты», не в «неразобранное» (N6.4)
            project.SetRoles(goal.RoleIds);
            await placer.PlaceAsync(project, new SomedayTarget(null, 0), ct);
            tasks.Add(project);
            await unitOfWork.SaveChangesAsync(ct); // у проекта появляется Id — цель ссылается на него
            await SetProjectFromThisWeekOnAsync(goal, project, ct);
            created = project;
        }

        var step = TaskItem.CreateStep(project, title, Now);
        step.SetRoles(project.RoleIds); // S5: копия ролей проекта
        await projects.PlaceStepAsync(step, project.Id, int.MaxValue, ct);
        tasks.Add(step);
        await unitOfWork.SaveChangesAsync(ct);
        return new GoalStep(goal, step, created);
    }

    /// <summary>Выбрать проект цели — как в окне: постоянной цели с этой недели и дальше (шаблон серии и уже созданные цели), P4.</summary>
    private async Task SetProjectFromThisWeekOnAsync(WeeklyGoal goal, TaskItem project, CancellationToken ct)
    {
        goal.SetProject(project);
        if (goal.RepeatsAfterThisWeek)
        {
            goal.Series!.CopyTemplateFrom(goal);
            foreach (var later in await LaterInSeriesAsync(goal, ct))
            {
                later.SetProject(project);
            }
        }
    }

    /// <summary>«🫙 копить» по умолчанию, когда цели включают «каждую неделю»: есть роль-измерение пилы — копить, иначе нет.</summary>
    private async Task<bool> HabitByDefaultAsync(WeeklyGoal goal, CancellationToken ct)
    {
        foreach (var roleId in goal.RoleIds)
        {
            if (await roles.FindAsync(roleId, ct) is { IsSharpenSaw: true })
            {
                return true;
            }
        }

        return false;
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
            session = Nearest(open, today) ?? await sessions.CreateAsync(goal, ct);

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
    /// Перетаскивание цели в список роли <paramref name="roleId"/> на позицию <paramref name="index"/> (та же неделя).
    /// Из той же роли — просто порядок. Из другой роли (<paramref name="fromRoleId"/>): <paramref name="addRole"/> = false —
    /// перенос, роль-источник заменяется новой; true (с Shift) — роль добавляется, остальные остаются.
    /// Постоянная цель меняет роли и на следующих неделях.
    /// </summary>
    public async Task<WeeklyGoal> MoveAsync(
        int id, int roleId, int index, int? fromRoleId = null, bool addRole = false, CancellationToken ct = default)
    {
        var goal = await GetAsync(id, ct);
        await GetRoleAsync(roleId, ct);

        var rolesBefore = goal.RoleIds;
        goal.AddRole(roleId);
        var from = fromRoleId ?? (rolesBefore.Count == 1 ? rolesBefore[0] : roleId);
        if (!addRole && from != roleId && goal.HasRole(from))
        {
            goal.RemoveRole(from);
        }

        await ordering.PlaceAsync(goal, roleId, index, ct: ct);
        if (!goal.RoleIds.SequenceEqual(rolesBefore) && goal.RepeatsAfterThisWeek)
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
        var session = goal.UnscheduledSession ?? await sessions.CreateAsync(goal, ct);

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

        if (task.IsSession)
        {
            throw new DomainException("Это дело уже связано с целью.");
        }

        var goal = WeeklyGoal.Create(roleId, weekStart, task.Title);
        goal.CopyTemplateFrom(task);
        if (task.State == TaskState.Someday || (task.PlannedDate is { } day && Week.StartOf(day) != Week.StartOf(weekStart)))
        {
            task.PlanForWeek(); // мысль из ленты или дело из дня другой недели — занятие без дня
        }

        goal.AddSession(task);

        await ordering.PlaceAsync(goal, roleId, index, ct: ct);
        goals.Add(goal);
        if (role.IsSharpenSaw)
        {
            await StartRecurringSeriesAsync(goal, ct);
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
            task.SetRoles(goal.RoleIds);
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

    /// <summary>Ближайшее открытое занятие: сегодняшнее, иначе самое раннее по дню и времени, иначе без дня.</summary>
    private static TaskItem? Nearest(List<TaskItem> open, DateOnly today) =>
        open.FirstOrDefault(t => t.PlannedDate == today)
        ?? open.Where(t => t.PlannedDate is not null)
            .OrderBy(t => t.PlannedDate).ThenBy(t => t.StartTime ?? TimeOnly.MaxValue).ThenBy(t => t.SortOrder)
            .FirstOrDefault()
        ?? open.FirstOrDefault(t => t.PlannedDate is null);

    private async Task<WeeklyGoal> GetAsync(int id, CancellationToken ct) =>
        await goals.FindAsync(id, ct) ?? throw new NotFoundException("Цель", id);

    private async Task<Role> GetRoleAsync(int roleId, CancellationToken ct) =>
        await roles.FindAsync(roleId, ct) ?? throw new NotFoundException("Роль", roleId);
}
