using Planner.Shared;

namespace Planner.Client.Services;

// Левая панель: роли (и измерения пилы) и их цели на неделю
public partial class PlannerState
{
    /// <summary>
    /// Не разложено (N1): «раз в неделю» минус занятия в днях недели цели и выполненные без дня (корзина не в счёт), не меньше 0.
    /// Выполненная цель и цель прошедшей недели — 0 (подсветки нет, N1.1, N1.4). Только вид: считается по занятиям цели.
    /// </summary>
    public static int UnplacedOf(WeeklyGoalDto goal, DateOnly today)
    {
        var weekEnd = goal.WeekStart.AddDays(6);
        if (goal.IsDone || weekEnd < today)
        {
            return 0;
        }

        var placed = goal.Sessions.Count(s =>
            s.PlannedDate is { } date ? date >= goal.WeekStart && date <= weekEnd : s.IsDone);
        return Math.Max(0, goal.TimesPerWeek - placed);
    }

    /// <summary>Цвет для новой роли: следующий по кругу палитры.</summary>
    public string NextRoleColor =>
        RolePalette.Count == 0 ? "#9fd3bd" : RolePalette[UserRoles.Count() % RolePalette.Count];

    // ---------- Цели ----------

    /// <summary>«+ цель»: новая цель роли на показанную неделю, в конец.</summary>
    public async Task<bool> CreateGoalAsync(int roleId, string title)
    {
        title = title.Trim();
        if (title.Length == 0)
        {
            return false;
        }

        var temp = new WeeklyGoalDto(NextTempId(), [new GoalRoleDto(roleId, NextGoalOrder(roleId))], WeekStart, title,
            TimesPerWeek: 1, StartTime: null, Quadrant: null, Notes: null, DurationMinutes: null,
            Sessions: [], IsRecurring: FindRole(roleId)?.IsSharpenSaw ?? false); // как решит сервер
        Goals.Add(temp);

        var ok = await SaveAsync(async () =>
        {
            var saved = await api.CreateGoalAsync(new CreateGoalRequest(roleId, WeekStart, title));
            var index = Goals.FindIndex(g => g.Id == temp.Id);
            if (index >= 0)
            {
                Goals[index] = saved with { Roles = temp.Roles };
            }
        });

        if (!ok && Goals.RemoveAll(g => g.Id == temp.Id) > 0)
        {
            NotifyChanged();
        }

        if (ok)
        {
            await RefreshHabitsIfSawAsync([roleId]); // постоянная цель пилы — новая привычка
        }

        return ok;
    }

    /// <summary>
    /// Что уберёт выключение «каждую неделю» у цели (fixes-2026-10.md, E1.2) — для вопроса перед «Сохранить».
    /// Сервер не ответил — null: окно сохраняется без вопроса (ошибку покажет само сохранение).
    /// </summary>
    public async Task<SeriesEndPreviewDto?> SeriesEndPreviewAsync(int goalId)
    {
        try
        {
            return await api.GetSeriesEndPreviewAsync(goalId);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Сохранить окно цели. Цель меняется сразу; занятия в днях (по шаблону у них могли поменяться
    /// название, время, квадрант, заметки, длительность, роль) — по ответу сервера, он решает, какие поля «свои».
    /// </summary>
    public async Task UpdateGoalAsync(int goalId, UpdateGoalRequest form)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal || string.IsNullOrWhiteSpace(form.Title))
        {
            return;
        }

        // Новые роли — цель встаёт в конец их списков, в прежних остаётся на месте (так же делает сервер)
        var roles = form.RoleIds.Distinct()
            .Select(id => goal.Roles.FirstOrDefault(r => r.RoleId == id) ?? new GoalRoleDto(id, NextGoalOrder(id)))
            .OrderBy(r => r.RoleId).ToList();
        ReplaceGoal(goal with
        {
            Title = form.Title.Trim(),
            Roles = roles,
            TimesPerWeek = form.TimesPerWeek,
            StartTime = form.StartTime,
            Quadrant = form.Quadrant,
            Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            DurationMinutes = form.DurationMinutes,
            IsRecurring = form.IsRecurring ?? goal.IsRecurring,
            AllowSeveralPerDay = form.AllowSeveralPerDay ?? goal.AllowSeveralPerDay,
            Days = form.Days is { } d ? d == 0 ? null : d : goal.Days,
        });

        // Дни поменялись — занятия разложил сервер (добавил и убрал в корзину) — потом перечитываем
        SetSessionRoles(goal, form.RoleIds);
        var daysChanged = form.Days is { } newDays && (newDays == 0 ? null : newDays) != goal.Days;
        IReadOnlyList<int> removed = [];
        var ok = await SaveAsync(async () =>
        {
            var result = await api.UpdateGoalAsync(goalId, form);
            ReplaceGoal(result.Goal);
            foreach (var session in result.Sessions.Where(s => s.PlannedDate is { } d && WeekDays.Contains(d)))
            {
                _tasks[session.Id] = session;
            }

            removed = result.RemovedSessionIds ?? [];
        });

        if (ok && daysChanged)
        {
            await ReloadAsync();
        }
        else if (ok)
        {
            await RefreshHabitsIfSawAsync(form.RoleIds, goal, form.IsHabit == true); // норма, «каждую неделю», «копить», роли — новые итоги
        }

        if (ok && removed.Count > 0)
        {
            var ids = removed;
            notifier.WithAction($"Убрано занятий: {ids.Count}", "Вернуть", () => RestoreSessionsAsync(ids), UndoWindow);
        }
    }

    /// <summary>«Вернуть» после смены дней: убранные занятия — из корзины на свои дни.</summary>
    private async Task RestoreSessionsAsync(IReadOnlyList<int> taskIds)
    {
        await SaveAsync(async () =>
        {
            foreach (var id in taskIds)
            {
                await api.RestoreTaskAsync(id);
            }
        });
        await ReloadAsync();
    }

    /// <summary>
    /// Клик по отметке цели: сервер отмечает ближайшее занятие (или добавляет выполненное на сегодня),
    /// а у выполненной цели снимает отметку с последнего занятия. Какое занятие он выберет, решает сервер,
    /// поэтому здесь ждём ответа, а не угадываем.
    /// </summary>
    public async Task ToggleGoalAsync(int goalId)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal)
        {
            return;
        }

        // Отмечать нечего, кроме нового занятия на сегодня, а сегодня оно уже есть (так же откажет сервер)
        var today = RuDates.Today;
        if (!goal.IsDone && !goal.AllowSeveralPerDay && WeekStart == RuDates.WeekStartOf(today)
            && !goal.Sessions.Any(s => !s.IsDone && s.PlannedDate is not null)
            && goal.Sessions.Any(s => s.PlannedDate == today))
        {
            notifier.Info("сегодня уже отмечено");
            return;
        }

        TaskDto? changed = null;
        var ok = await SaveAsync(async () =>
        {
            var result = await api.ToggleGoalAsync(goalId, new ToggleGoalRequest(RuDates.Today));
            ReplaceGoal(result.Goal);
            changed = result.Task;
            if (result.Task is { } session)
            {
                if (session.PlannedDate is { } date && WeekDays.Contains(date))
                {
                    _tasks[session.Id] = session;
                }
                else
                {
                    _tasks.Remove(session.Id);
                }
            }
        });

        if (ok && changed is not null && HabitOfGoal(goal) is { } habit)
        {
            await AfterHabitTaskToggledAsync(habit.Id, changed.State == TaskStateDto.Done, changed.PlannedDate ?? goal.WeekStart);
        }
    }

    /// <summary>
    /// Удалить цель. Выполненные занятия остаются в неделе, невыполненные уходят в корзину,
    /// занятие без дня возвращается в ленту (без своей категории — в «неразобранное»).
    /// С «Вернуть»; после удаления на сервере — перечитываем: занятие без дня пришло в ленту.
    /// Постоянная цель: <paramref name="fromThisWeekOn"/> — серия заканчивается (с этой недели и дальше), иначе — только эта неделя.
    /// </summary>
    public async Task DeleteGoalAsync(int goalId, bool fromThisWeekOn = false)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal)
        {
            return;
        }

        var scope = !goal.IsRecurring ? "" : fromThisWeekOn ? " — с этой недели и дальше" : " — на этой неделе";
        await DeleteWithUndoAsync(Deleted(goal.Title) + scope, () => RemoveGoalLocally(goal),
            () => api.DeleteGoalAsync(goalId, fromThisWeekOn), reloadAfter: goal.Sessions.Count > 0 || HabitOfGoal(goal) is not null);
    }

    // ---------- Роли ----------

    public async Task<RoleDto?> CreateRoleAsync(string name, string color)
    {
        await CommitPendingDeleteAsync();
        try
        {
            var created = await api.CreateRoleAsync(name.Trim(), color);
            Roles.Add(created);
            NotifyChanged();
            return created;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return null;
        }
    }

    /// <summary>Переименовать (кроме измерений пилы) и/или сменить цвет.</summary>
    public async Task UpdateRoleAsync(int roleId, string name, string color)
    {
        if (FindRole(roleId) is not { } role)
        {
            return;
        }

        name = name.Trim();
        var newName = role.IsSharpenSaw || name == role.Name ? null : name;
        ReplaceRole(role with { Name = newName ?? role.Name, Color = color });
        await SaveAsync(() => api.UpdateRoleAsync(roleId, new UpdateRoleRequest(newName, color)));
    }

    /// <summary>Перетаскивание роли: новое место среди пользовательских ролей (index — без самой роли).</summary>
    public async Task MoveRoleToAsync(int roleId, int index)
    {
        var order = UserRoles.ToList();
        var current = order.FindIndex(r => r.Id == roleId);
        if (current < 0)
        {
            return; // измерения пилы не переставляются
        }

        var role = order[current];
        order.RemoveAt(current);
        index = Math.Clamp(index, 0, order.Count);
        if (index == current)
        {
            return;
        }

        order.Insert(index, role);
        for (var i = 0; i < order.Count; i++)
        {
            ReplaceRole(order[i] with { SortOrder = i });
        }

        await SaveAsync(() => api.ReorderRolesAsync(order.Select(r => r.Id).ToList()));
    }

    // ---------- Перетаскивание целей ----------

    /// <summary>
    /// Цель перетащили в список роли <paramref name="roleId"/> на позицию <paramref name="index"/>. Из той же роли — порядок.
    /// Из другой роли (<paramref name="fromRoleId"/>): обычный перенос — роль-источник заменяется новой;
    /// <paramref name="addRole"/> (с Shift) — роль добавляется, остальные остаются. Роли занятий — роли цели.
    /// </summary>
    public async Task MoveGoalAsync(int goalId, int roleId, int index, int? fromRoleId = null, bool addRole = false)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal || FindRole(roleId) is null)
        {
            return;
        }

        // Те же правила, что у сервера
        var from = fromRoleId ?? (goal.Roles.Count == 1 ? goal.Roles[0].RoleId : roleId);
        var roleIds = goal.RoleIds.Append(roleId)
            .Where(id => addRole || from == roleId || id != from)
            .Distinct().Order().ToList();
        var siblings = GoalsFor(roleId).Where(g => g.Id != goalId).ToList();
        index = Math.Clamp(index, 0, siblings.Count);
        var rolesChanged = !roleIds.SequenceEqual(goal.RoleIds.Order());
        if (!rolesChanged && GoalsFor(roleId).ToList().FindIndex(g => g.Id == goalId) == index)
        {
            return; // бросили туда же
        }

        var moved = goal with { Roles = [.. goal.Roles.Where(r => roleIds.Contains(r.RoleId))] };
        siblings.Insert(index, moved);
        for (var i = 0; i < siblings.Count; i++)
        {
            ReplaceGoal(WithPlace(siblings[i], roleId, i));
        }

        SetSessionRoles(goal, roleIds);
        if (await SaveAsync(() => api.MoveGoalAsync(goalId, new MoveGoalRequest(roleId, index, from, addRole))) && rolesChanged)
        {
            await RefreshHabitsIfSawAsync(roleIds, goal); // цель могла войти в пилу или выйти из неё
        }
    }

    /// <summary>
    /// Дело из ленты или из дня → роль: оно становится целью недели («камнем») и её первым занятием.
    /// Дело из ленты уходит из ленты (занятие без дня); дело из дня остаётся в своём дне.
    /// Если дело уже занятие цели — просто переносим ту цель.
    /// </summary>
    public async Task CreateGoalFromTaskAsync(int taskId, int roleId, int index)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || FindRole(roleId) is null)
        {
            return;
        }

        if (GoalOfTask(taskId) is { } existing)
        {
            await MoveGoalAsync(existing.Id, roleId, index);
            return;
        }

        // Локально: цель с временным Id на месте, дело — её занятие
        var siblings = GoalsFor(roleId).ToList();
        index = Math.Clamp(index, 0, siblings.Count);
        var tempGoalId = NextTempId();
        var session = task with
        {
            State = task.State == TaskStateDto.Someday ? TaskStateDto.Planned : task.State,
            RoleIds = [roleId],
            GoalId = tempGoalId,
        };
        // Шаблон цели — из дела (так же делает сервер)
        var temp = new WeeklyGoalDto(tempGoalId, [new GoalRoleDto(roleId, 0)], WeekStart, task.Title, TimesPerWeek: 1,
            task.StartTime, task.Quadrant, task.Notes, task.DurationMinutes,
            Sessions: [ToSession(session)], IsRecurring: FindRole(roleId)?.IsSharpenSaw ?? false);
        siblings.Insert(index, temp);
        Goals.Add(temp);
        for (var i = 0; i < siblings.Count; i++)
        {
            ReplaceGoal(WithPlace(siblings[i], roleId, i));
        }

        _tasks[taskId] = session;

        var ok = await SaveAsync(async () =>
        {
            var result = await api.CreateGoalFromTaskAsync(new GoalFromTaskRequest(taskId, roleId, WeekStart, index));
            ReplaceTempGoal(temp.Id, result.Goal);
            if (result.Task is { } saved)
            {
                _tasks[taskId] = saved;
            }
        });

        if (ok)
        {
            await RefreshHabitsIfSawAsync([roleId]);
        }
    }

    /// <summary>
    /// Цель → день: новое занятие (дело-«камень») в этот день. Если у цели есть занятие без дня —
    /// в день встаёт оно. Перенос уже стоящего занятия — перетаскиванием самого дела.
    /// </summary>
    public async Task ScheduleGoalAsync(int goalId, DateOnly date, int index)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal)
        {
            return;
        }

        // В этот день у цели уже есть занятие — новое не создаём (если не разрешено несколько в день)
        if (!goal.AllowSeveralPerDay && goal.Sessions.FirstOrDefault(s => s.PlannedDate == date) is { } existing)
        {
            RefuseDayTaken(existing.TaskId);
            return;
        }

        var unscheduled = goal.Sessions.FirstOrDefault(s => s.PlannedDate is null && !s.IsDone);
        var task = unscheduled is null
            ? NewSessionTask(goal, NextTempId())
            : FindTask(unscheduled.TaskId) is { } shown
                ? shown with { StartTime = shown.StartTime ?? goal.StartTime }
                : NewSessionTask(goal, unscheduled.TaskId);

        PlaceInDayLocally(task, date, index);
        SyncSession(_tasks[task.Id]);

        await SaveAsync(async () =>
        {
            var result = await api.ScheduleGoalAsync(goalId, new DayTargetDto(date, index));
            if (result.Task is { } saved)
            {
                ReplaceTempTask(task.Id, saved);
            }

            ReplaceGoal(result.Goal);
        });
    }

    /// <summary>
    /// Цель → лента: цель исчезает, её занятие без дня (или новое дело с её названием) встаёт в категорию.
    /// Остальные занятия — как при удалении цели.
    /// </summary>
    public async Task MoveGoalToFeedAsync(int goalId, int? categoryId, int index)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal || !IsFeedPlace(categoryId))
        {
            return;
        }

        var unscheduled = goal.Sessions.FirstOrDefault(s => s.PlannedDate is null && !s.IsDone);
        var task = unscheduled is not null && FindTask(unscheduled.TaskId) is { } shown
            ? shown with { GoalId = null }
            : new TaskDto(unscheduled?.TaskId ?? NextTempId(), goal.Title, null, categoryId, goal.RoleIds, null, null,
                TaskStateDto.Someday, null, null, null, 0, null);

        PlaceInFeedLocally(task, categoryId, index);
        Goals.RemoveAll(g => g.Id == goalId);
        var hasOtherSessions = goal.Sessions.Any(s => s != unscheduled);

        await SaveAsync(async () =>
        {
            var saved = await api.MoveGoalToSomedayAsync(goalId, new GoalToSomedayRequest(categoryId, index));
            ReplaceTempTask(task.Id, saved);
            if (hasOtherSessions)
            {
                await ReloadAsync(); // занятия в днях ушли в корзину или стали обычными делами
            }
        });
    }

    /// <summary>
    /// Удалить роль. Цели, у которых она единственная, удаляются (на всех неделях); занятия — как при удалении цели,
    /// занятия без дня возвращаются в ленту (без своей категории — в «неразобранное»). У целей с другими ролями роль просто убирается.
    /// С «Вернуть»; после удаления на сервере — перечитываем всё (цели других недель, лента).
    /// </summary>
    public async Task DeleteRoleAsync(int roleId)
    {
        if (FindRole(roleId) is not { } role)
        {
            return;
        }

        await DeleteWithUndoAsync(Deleted(role.Name), () =>
        {
            Roles.RemoveAll(r => r.Id == roleId);
            foreach (var goal in GoalsFor(roleId).ToList())
            {
                if (goal.Roles.Count > 1)
                {
                    ReplaceGoal(goal with { Roles = [.. goal.Roles.Where(r => r.RoleId != roleId)] });
                }
                else
                {
                    RemoveGoalLocally(goal);
                }
            }

            foreach (var task in _tasks.Values.Where(t => t.RoleIds.Contains(roleId)).ToList())
            {
                _tasks[task.Id] = task with { RoleIds = [.. task.RoleIds.Where(id => id != roleId)] };
            }
        }, () => api.DeleteRoleAsync(roleId), reloadAfter: true);
    }

    // ---------- Помощники ----------

    public WeeklyGoalDto? FindGoal(int goalId) => Goals.FirstOrDefault(g => g.Id == goalId);

    /// <summary>Занятия цели, которые есть на экране (в днях показанной недели).</summary>
    private IEnumerable<TaskDto> SessionTasks(WeeklyGoalDto goal) =>
        goal.Sessions.Select(s => FindTask(s.TaskId)).OfType<TaskDto>().ToList();

    /// <summary>Новое занятие по шаблону цели (время — когда встанет в день, так же делает сервер).</summary>
    private static TaskDto NewSessionTask(WeeklyGoalDto goal, int id) =>
        new(id, goal.Title, goal.Notes, null, goal.RoleIds, goal.Id, goal.Quadrant, TaskStateDto.Planned,
            null, goal.StartTime, goal.DurationMinutes, 0, null);

    private static GoalSessionDto ToSession(TaskDto task) =>
        new(task.Id, task.PlannedDate, task.State == TaskStateDto.Done);

    /// <summary>
    /// Занятие поменялось локально (день, отметка) — обновить его в цели, чтобы прогресс
    /// и метки дней сразу показали новое. Так же сервер строит список занятий.
    /// </summary>
    private void SyncSession(TaskDto task)
    {
        if (GoalOfTask(task.Id) is { } goal)
        {
            var sessions = goal.Sessions.Where(s => s.TaskId != task.Id).ToList();
            if (task.State is TaskStateDto.Planned or TaskStateDto.Done)
            {
                sessions.Add(ToSession(task));
            }

            ReplaceGoal(goal with
            {
                Sessions = [.. sessions.OrderBy(s => s.PlannedDate ?? DateOnly.MaxValue).ThenBy(s => s.TaskId)],
            });
        }
    }

    /// <summary>Дело больше не занятие (ушло в корзину или в ленту) — убрать его из прогресса цели.</summary>
    private void RemoveSession(int taskId)
    {
        for (var i = 0; i < Goals.Count; i++)
        {
            if (Goals[i].Sessions.Any(s => s.TaskId == taskId))
            {
                Goals[i] = Goals[i] with { Sessions = [.. Goals[i].Sessions.Where(s => s.TaskId != taskId)] };
            }
        }
    }

    /// <summary>Заменить временную цель сохранённой, сохранив локальный порядок.</summary>
    private void ReplaceTempGoal(int tempId, WeeklyGoalDto saved)
    {
        var index = Goals.FindIndex(g => g.Id == tempId);
        if (index >= 0)
        {
            Goals[index] = saved with { Roles = Goals[index].Roles };
        }
    }

    /// <summary>Цель на месте <paramref name="sortOrder"/> в списке роли (роль добавляется, если её не было).</summary>
    private static WeeklyGoalDto WithPlace(WeeklyGoalDto goal, int roleId, int sortOrder) => goal with
    {
        Roles = [.. goal.Roles.Where(r => r.RoleId != roleId).Append(new GoalRoleDto(roleId, sortOrder)).OrderBy(r => r.RoleId)],
    };

    /// <summary>Место в конце списка роли.</summary>
    private int NextGoalOrder(int roleId) =>
        GoalsFor(roleId).Select(g => g.SortOrderIn(roleId)).DefaultIfEmpty(-1).Max() + 1;

    /// <summary>Роли занятий — роли цели (так же решает сервер).</summary>
    private void SetSessionRoles(WeeklyGoalDto goal, IEnumerable<int> roleIds)
    {
        List<int> ordered = [.. roleIds.Distinct().Order()];
        foreach (var task in SessionTasks(goal))
        {
            _tasks[task.Id] = task with { RoleIds = ordered };
        }
    }

    private void ReplaceGoal(WeeklyGoalDto goal)
    {
        var index = Goals.FindIndex(g => g.Id == goal.Id);
        if (index >= 0)
        {
            Goals[index] = goal;
        }
    }

    private void ReplaceRole(RoleDto role)
    {
        var index = Roles.FindIndex(r => r.Id == role.Id);
        if (index >= 0)
        {
            Roles[index] = role;
        }
    }
}
