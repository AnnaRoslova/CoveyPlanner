using Planner.Shared;

namespace Planner.Client.Services;

// Левая панель: роли (и измерения пилы) и их цели на неделю
public partial class PlannerState
{
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

        var siblings = GoalsFor(roleId).ToList();
        var temp = new WeeklyGoalDto(NextTempId(), roleId, WeekStart, title, IsDone: false,
            SortOrder: siblings.Count == 0 ? 0 : siblings.Max(g => g.SortOrder) + 1,
            TaskId: null, PlannedDate: null, StartTime: null);
        Goals.Add(temp);

        var ok = await SaveAsync(async () =>
        {
            var saved = await api.CreateGoalAsync(new CreateGoalRequest(roleId, WeekStart, title));
            var index = Goals.FindIndex(g => g.Id == temp.Id);
            if (index >= 0)
            {
                Goals[index] = saved with { SortOrder = temp.SortOrder };
            }
        });

        if (!ok && Goals.RemoveAll(g => g.Id == temp.Id) > 0)
        {
            NotifyChanged();
        }

        return ok;
    }

    /// <summary>Переименовать цель; связанное дело переименовывается вместе с ней.</summary>
    public async Task RenameGoalAsync(int goalId, string title)
    {
        title = title.Trim();
        if (goalId < 0 || FindGoal(goalId) is not { } goal || title.Length == 0 || title == goal.Title)
        {
            return;
        }

        ReplaceGoal(goal with { Title = title });
        if (goal.TaskId is { } taskId && FindTask(taskId) is { } task)
        {
            _tasks[taskId] = task with { Title = title };
        }

        await SaveAsync(() => api.UpdateGoalAsync(goalId, new UpdateGoalRequest(title, null)));
    }

    /// <summary>Отметка «выполнено» у цели; связанное дело отмечается вместе с ней.</summary>
    public async Task SetGoalDoneAsync(int goalId, bool isDone)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal)
        {
            return;
        }

        ReplaceGoal(goal with { IsDone = isDone });
        if (goal.TaskId is { } taskId && FindTask(taskId) is { } task)
        {
            _tasks[taskId] = task with { State = isDone ? TaskStateDto.Done : TaskStateDto.Planned };
        }

        await SaveAsync(() => api.UpdateGoalAsync(goalId, new UpdateGoalRequest(null, isDone)));
    }

    /// <summary>
    /// Удалить цель. Если связанное дело не стоит в дне, оно возвращается в ленту;
    /// когда у дела нет своей категории — <see cref="ReturnResult.NeedsCategory"/>.
    /// Ждём ответа сервера, а не удаляем сразу: иначе при 409 пришлось бы возвращать цель на место.
    /// </summary>
    public async Task<ReturnResult> DeleteGoalAsync(int goalId, int? categoryId = null)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal)
        {
            return ReturnResult.Failed;
        }

        var result = await RunReturningToFeedAsync(() => api.DeleteGoalAsync(goalId, categoryId));
        if (result == ReturnResult.Done)
        {
            Goals.RemoveAll(g => g.Id == goalId);

            // Дело без дня ушло в ленту — перечитываем ленту с сервера
            if (goal.TaskId is not null && goal.PlannedDate is null)
            {
                await ReloadAsync();
            }

            NotifyChanged();
        }

        return result;
    }

    // ---------- Роли ----------

    public async Task<RoleDto?> CreateRoleAsync(string name, string color)
    {
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

    /// <summary>Цель → другое место в своей роли или в другой роли (дело цели получает эту роль).</summary>
    public async Task MoveGoalAsync(int goalId, int roleId, int index)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal || FindRole(roleId) is null)
        {
            return;
        }

        var siblings = GoalsFor(roleId).Where(g => g.Id != goalId).ToList();
        index = Math.Clamp(index, 0, siblings.Count);
        if (goal.RoleId == roleId && GoalsFor(roleId).ToList().FindIndex(g => g.Id == goalId) == index)
        {
            return; // бросили туда же
        }

        siblings.Insert(index, goal with { RoleId = roleId });
        for (var i = 0; i < siblings.Count; i++)
        {
            ReplaceGoal(siblings[i] with { SortOrder = i });
        }

        if (goal.TaskId is { } taskId && FindTask(taskId) is { } task)
        {
            _tasks[taskId] = task with { RoleId = roleId };
        }

        await SaveAsync(() => api.MoveGoalAsync(goalId, new MoveGoalRequest(roleId, index)));
    }

    /// <summary>
    /// Дело из ленты или из дня → роль: оно становится целью недели («камнем»).
    /// Дело из ленты уходит из ленты (запланировано на неделю без дня); дело из дня остаётся в своём дне.
    /// Если дело уже связано с целью — просто переносим ту цель.
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

        // Локально: цель с временным Id на месте; дело из ленты — «на неделю без дня»
        var siblings = GoalsFor(roleId).ToList();
        index = Math.Clamp(index, 0, siblings.Count);
        var temp = new WeeklyGoalDto(NextTempId(), roleId, WeekStart, task.Title, task.State == TaskStateDto.Done,
            SortOrder: 0, taskId, task.PlannedDate, task.StartTime);
        siblings.Insert(index, temp);
        Goals.Add(temp);
        for (var i = 0; i < siblings.Count; i++)
        {
            ReplaceGoal(siblings[i] with { SortOrder = i });
        }

        _tasks[taskId] = task.State == TaskStateDto.Someday
            ? task with { State = TaskStateDto.Planned, RoleId = roleId }
            : task with { RoleId = roleId };

        await SaveAsync(async () =>
        {
            var result = await api.CreateGoalFromTaskAsync(new GoalFromTaskRequest(taskId, roleId, WeekStart, index));
            ReplaceTempGoal(temp.Id, result.Goal);
        });
    }

    /// <summary>Цель → день: связанное дело переносится в этот день, а если дела ещё нет — появляется новое.</summary>
    public async Task ScheduleGoalAsync(int goalId, DateOnly date, int index)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal)
        {
            return;
        }

        // Дело цели: уже на экране (в одном из дней) или ещё нет — тогда временное по данным цели
        var task = goal.TaskId is { } taskId && FindTask(taskId) is { } shown
            ? shown
            : new TaskDto(goal.TaskId ?? NextTempId(), goal.Title, null, null, goal.RoleId, null,
                goal.IsDone ? TaskStateDto.Done : TaskStateDto.Planned, null, null, 0, null);

        PlaceInDayLocally(task, date, index);
        ReplaceGoal(goal with { TaskId = task.Id, PlannedDate = date, StartTime = _tasks[task.Id].StartTime });

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

    /// <summary>Цель → лента: цель исчезает, её дело (или новое с её названием) встаёт в категорию.</summary>
    public async Task MoveGoalToFeedAsync(int goalId, int categoryId, int index)
    {
        if (goalId < 0 || FindGoal(goalId) is not { } goal || FindCategory(categoryId) is null)
        {
            return;
        }

        var task = goal.TaskId is { } taskId && FindTask(taskId) is { } shown
            ? shown
            : new TaskDto(goal.TaskId ?? NextTempId(), goal.Title, null, categoryId, goal.RoleId, null,
                TaskStateDto.Someday, null, null, 0, null);

        PlaceInFeedLocally(task, categoryId, index);
        Goals.RemoveAll(g => g.Id == goalId);

        await SaveAsync(async () =>
        {
            var saved = await api.MoveGoalToSomedayAsync(goalId, new GoalToSomedayRequest(categoryId, index));
            ReplaceTempTask(task.Id, saved);
        });
    }

    /// <summary>
    /// Удалить роль вместе с её целями (на всех неделях). Дела, висевшие на целях без дня,
    /// возвращаются в ленту — без своей категории нужна одна категория на все
    /// (<see cref="ReturnResult.NeedsCategory"/>). У остальных дел роль просто снимается.
    /// </summary>
    public async Task<ReturnResult> DeleteRoleAsync(int roleId, int? categoryId = null)
    {
        var result = await RunReturningToFeedAsync(() => api.DeleteRoleAsync(roleId, categoryId));
        if (result == ReturnResult.Done)
        {
            await ReloadAsync(); // цели, роли у дел и лента поменялись — берём всё с сервера
        }

        return result;
    }

    // ---------- Помощники ----------

    public WeeklyGoalDto? FindGoal(int goalId) => Goals.FirstOrDefault(g => g.Id == goalId);

    /// <summary>Вызов, который может ответить «нужна категория» (409).</summary>
    private async Task<ReturnResult> RunReturningToFeedAsync(Func<Task> serverCall)
    {
        try
        {
            await serverCall();
            return ReturnResult.Done;
        }
        catch (ApiException ex) when (ex.IsCategoryRequired)
        {
            return ReturnResult.NeedsCategory;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return ReturnResult.Failed;
        }
    }

    /// <summary>Заменить временную цель сохранённой, сохранив локальный порядок.</summary>
    private void ReplaceTempGoal(int tempId, WeeklyGoalDto saved)
    {
        var index = Goals.FindIndex(g => g.Id == tempId);
        if (index >= 0)
        {
            Goals[index] = saved with { SortOrder = Goals[index].SortOrder };
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
