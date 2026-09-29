using Planner.Shared;

namespace Planner.Client.Services;

// План недели: дела в днях — создать, отметить, изменить
public partial class PlannerState
{
    public TaskDto? FindTask(int taskId) => _tasks.GetValueOrDefault(taskId);

    /// <summary>Цель недели, с которой связано дело (оно — «камень»).</summary>
    public WeeklyGoalDto? GoalOfTask(int taskId) => Goals.FirstOrDefault(g => g.TaskId == taskId);

    /// <summary>«+ дело» внизу дня: в конец дел без времени. false — не сохранилось.</summary>
    public async Task<bool> CreateDayTaskAsync(string title, DateOnly date)
    {
        title = title.Trim();
        if (title.Length == 0)
        {
            return false;
        }

        var temp = new TaskDto(NextTempId(), title, null, null, null, null, TaskStateDto.Planned, date, null,
            SortOrder: NextUntimedOrder(date), TrashedAt: null);
        _tasks[temp.Id] = temp;

        var ok = await SaveAsync(async () =>
        {
            // «Очень большая» позиция — в конец (сервер прижимает к краю)
            var saved = await api.CreateTaskAsync(title, new DayTargetDto(date, int.MaxValue));
            ReplaceTempTask(temp.Id, saved);
        });

        if (!ok && _tasks.Remove(temp.Id))
        {
            NotifyChanged();
        }

        return ok;
    }

    /// <summary>Отметка «выполнено»; у связанной цели отметка меняется вместе с делом.</summary>
    public async Task SetDoneAsync(int taskId, bool isDone)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task)
        {
            return;
        }

        _tasks[taskId] = task with { State = isDone ? TaskStateDto.Done : TaskStateDto.Planned };
        UpdateGoalOfTask(taskId, g => g with { IsDone = isDone });
        await SaveAsync(() => api.SetTaskDoneAsync(taskId, isDone));
    }

    /// <summary>Сохранить окно редактирования. startTime = null — без времени.</summary>
    public async Task UpdateTaskAsync(
        int taskId, string title, string? notes, int? roleId, int? quadrant, TimeOnly? startTime)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        title = title.Trim();
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        // Время убрали — дело встаёт в конец ручного списка дня (так же делает сервер)
        var sortOrder = task.StartTime is not null && startTime is null && task.PlannedDate is { } date
            ? NextUntimedOrder(date)
            : task.SortOrder;

        _tasks[taskId] = task with
        {
            Title = title,
            Notes = notes,
            RoleId = roleId,
            Quadrant = quadrant,
            StartTime = startTime,
            SortOrder = sortOrder,
        };
        UpdateGoalOfTask(taskId, g => g with { Title = title, StartTime = startTime });

        await SaveAsync(() => api.UpdateTaskAsync(taskId,
            new UpdateTaskRequest(title, notes, roleId, quadrant, startTime)));
    }

    private int NextUntimedOrder(DateOnly date)
    {
        var untimed = TasksOn(date).Where(t => t.StartTime is null).ToList();
        return untimed.Count == 0 ? 0 : untimed.Max(t => t.SortOrder) + 1;
    }

    private void UpdateGoalOfTask(int taskId, Func<WeeklyGoalDto, WeeklyGoalDto> change)
    {
        var index = Goals.FindIndex(g => g.TaskId == taskId);
        if (index >= 0)
        {
            Goals[index] = change(Goals[index]);
        }
    }
}
