using Planner.Shared;

namespace Planner.Client.Services;

// «На неделе, без дня» (docs/specs/next-batch.md, N4, вариант Б; доработка — fixes-2026-10.md, B): дело запланировано на неделю, день не выбран.
// Показывается в левой панели той недели под своими ролями («на неделе · N»), без роли — внизу панели. Камнем не становится.
// Кнопка «→ на след. неделю» в окнах дела и мысли и бросок на заголовок роли — одно действие: плашка «… → след. неделя, «Роль» · Показать · Вернуть».
public partial class PlannerState
{
    /// <summary>Дела показанной недели без дня: с ролью <paramref name="roleId"/>; null — без роли (N4.2).</summary>
    public IReadOnlyList<TaskDto> WeekTasksWithoutDay(int? roleId) =>
        _tasks.Values
            .Where(t => t.PlannedWeek == WeekStart && t.PlannedDate is null && t.State is TaskStateDto.Planned or TaskStateDto.Done
                        && (roleId is { } id ? t.RoleIds.Contains(id) : t.RoleIds.Count == 0))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToList();

    /// <summary>
    /// «→ на след. неделю» (N4.1, B1–B3): на неделю после показанной, без дня; мысль уходит из ленты, дело — с дня.
    /// <paramref name="roleId"/> — роль из меню у дела без роли (дело её получает и встаёт под неё).
    /// </summary>
    public Task MoveToNextWeekAsync(int taskId, int? roleId = null) => MoveToWeekAsync(taskId, WeekStart.AddDays(7), roleId);

    /// <summary>Бросили на заголовок роли или на её «на неделе» (B5, N4.3): на показанную неделю без дня, с этой ролью.</summary>
    public Task MoveToWeekWithoutDayAsync(int taskId, int? roleId) => MoveToWeekAsync(taskId, WeekStart, roleId);

    private async Task MoveToWeekAsync(int taskId, DateOnly week, int? roleId)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task)
        {
            return;
        }

        if (task.GoalId is not null)
        {
            notifier.Info("у занятия цели своя неделя — неделя цели");
            return;
        }

        if (task.PlannedWeek == week && task.PlannedDate is null && (roleId is null || task.RoleIds.Contains(roleId.Value)))
        {
            return; // туда же
        }

        var origin = WeekOriginOf(task);
        var roles = roleId is { } id && !task.RoleIds.Contains(id) ? [.. task.RoleIds, id] : task.RoleIds;
        _tasks[taskId] = task with
        {
            State = task.State == TaskStateDto.Someday ? TaskStateDto.Planned : task.State,
            PlannedDate = null, StartTime = null, PlannedWeek = week, RoleIds = roles, SortOrder = int.MaxValue,
        };
        NotifyChanged();
        if (!await SaveAsync(async () => _tasks[taskId] = await api.MoveTaskAsync(taskId, new WeekTargetDto(week, roleId))))
        {
            return;
        }

        await ReloadAsync();

        // B4: «„Купить подарок“ → след. неделя, «Работа» · Показать · Вернуть»
        var role = roleId is { } chosen ? FindRole(chosen) : OrderedRoles.FirstOrDefault(r => roles.Contains(r.Id));
        var message = $"«{task.Title}» → {WeekWord(week)}" + (role is null ? "" : $", «{role.Name}»");
        notifier.WithActions(message, "Показать", () => ShowWeekTaskAsync(week, taskId), "Вернуть", () => UndoMoveToWeekAsync(taskId, origin),
            UndoWindow, nearTrash: origin.WasSomeday);
    }

    /// <summary>«след. неделя», «эта неделя» или «неделя 19–25 окт» — по сегодняшнему дню.</summary>
    private static string WeekWord(DateOnly week)
    {
        var thisWeek = RuDates.WeekStartOf(RuDates.Today);
        return week == thisWeek.AddDays(7) ? "след. неделя" : week == thisWeek ? "эта неделя" : $"неделя {RuDates.WeekRange(week)}";
    }

    /// <summary>«Показать»: перелистнуть на неделю дела и коротко подсветить его под ролью.</summary>
    private async Task ShowWeekTaskAsync(DateOnly week, int taskId)
    {
        if (WeekStart != week)
        {
            await GoToWeekAsync(week);
        }

        FlashTaskId = taskId;
        NotifyChanged();
        _ = UnflashLaterAsync(taskId);
    }

    /// <summary>Где было дело до переноса на неделю — для «Вернуть» (B4): место в ленте, в дне или в списке шагов, время, роли, прежняя неделя.</summary>
    private sealed record WeekOrigin(
        TaskDto Task, bool WasSomeday, int FeedIndex, DateOnly? Date, int DayIndex, DateOnly? Week, int? ParentId, int StepIndex);

    private WeekOrigin WeekOriginOf(TaskDto task) => new(
        task,
        task.State == TaskStateDto.Someday,
        SomedayIn(task.CategoryId).ToList().FindIndex(t => t.Id == task.Id),
        task.PlannedDate,
        task.PlannedDate is { } date ? TasksOn(date).Where(t => t.StartTime is null).ToList().FindIndex(t => t.Id == task.Id) : -1,
        task.PlannedWeek,
        task.ParentId,
        task.ParentId is { } parentId ? StepsOf(parentId).ToList().FindIndex(t => t.Id == task.Id) : -1);

    private async Task UndoMoveToWeekAsync(int taskId, WeekOrigin origin)
    {
        var task = origin.Task;
        await SaveAsync(async () =>
        {
            TaskTargetDto target = origin switch
            {
                { WasSomeday: true } => new SomedayTargetDto(task.CategoryId, Math.Max(origin.FeedIndex, 0)),
                { Date: { } date } => new DayTargetDto(date, origin.DayIndex < 0 ? int.MaxValue : origin.DayIndex),
                { Week: { } week } => new WeekTargetDto(week),
                { ParentId: { } parentId } => new StepListTargetDto(parentId, origin.StepIndex < 0 ? int.MaxValue : origin.StepIndex),
                _ => new SomedayTargetDto(task.CategoryId, 0),
            };
            await api.MoveTaskAsync(taskId, target);

            // Время снималось при переносе, роль могло добавить меню или бросок на роль
            await api.UpdateTaskAsync(taskId,
                new UpdateTaskRequest(task.Title, task.Notes, task.RoleIds, task.Quadrant, task.StartTime, task.DurationMinutes));
        });
        await ReloadAsync();
    }
}
