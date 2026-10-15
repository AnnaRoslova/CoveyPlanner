using Planner.Shared;

namespace Planner.Client.Services;

// «На неделе, без дня» (docs/specs/next-batch.md, N4, вариант Б): дело запланировано на неделю, день не выбран.
// Показывается в левой панели показанной недели под своей ролью («на неделе: N»), без роли — внизу панели. Камнем не становится.
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
    /// «→ на следующую неделю (без дня)» (N4.1): на неделю после показанной; мысль уходит из ленты, дело — с дня.
    /// Плашка «Перенесено на неделю 19–25 окт · Показать».
    /// </summary>
    public async Task MoveToNextWeekAsync(int taskId)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || task.GoalId is not null)
        {
            return;
        }

        var next = WeekStart.AddDays(7);
        _tasks.Remove(taskId); // с этой недели и из ленты дело уходит сразу
        if (await SaveAsync(async () => await api.MoveTaskAsync(taskId, new WeekTargetDto(next))))
        {
            await ReloadAsync();
            notifier.WithAction($"Перенесено на неделю {RuDates.WeekRange(next)}", "Показать", () => GoToWeekAsync(next), Notifier.DefaultDuration,
                nearTrash: task.State == TaskStateDto.Someday);
        }
    }

    /// <summary>Бросили на «на неделе» роли (или «без роли», roleId = null) показанной недели (N4.3): без дня на этой неделе, с этой ролью.</summary>
    public async Task MoveToWeekWithoutDayAsync(int taskId, int? roleId)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || task.GoalId is not null)
        {
            notifier.Info("у занятия цели своя неделя — неделя цели");
            return;
        }

        if (task.PlannedWeek == WeekStart && task.PlannedDate is null && (roleId is null || task.RoleIds.Contains(roleId.Value)))
        {
            return; // туда же
        }

        var roles = roleId is { } id && !task.RoleIds.Contains(id) ? [.. task.RoleIds, id] : task.RoleIds;
        _tasks[taskId] = task with
        {
            State = task.State == TaskStateDto.Someday ? TaskStateDto.Planned : task.State,
            PlannedDate = null, StartTime = null, PlannedWeek = WeekStart, RoleIds = roles, SortOrder = int.MaxValue,
        };
        if (await SaveAsync(async () => _tasks[taskId] = await api.MoveTaskAsync(taskId, new WeekTargetDto(WeekStart, roleId))))
        {
            await ReloadAsync();
        }
    }
}
