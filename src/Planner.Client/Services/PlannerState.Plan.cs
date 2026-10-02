using Planner.Shared;

namespace Planner.Client.Services;

// План недели: дела в днях — создать, отметить, изменить
public partial class PlannerState
{
    public TaskDto? FindTask(int taskId) => _tasks.GetValueOrDefault(taskId);

    /// <summary>Цель недели, занятием которой является дело (оно — «камень»).</summary>
    public WeeklyGoalDto? GoalOfTask(int taskId) => FindTask(taskId)?.GoalId is { } goalId ? FindGoal(goalId) : null;

    /// <summary>«+ дело» внизу дня: в конец дел без времени. false — не сохранилось.</summary>
    public async Task<bool> CreateDayTaskAsync(string title, DateOnly date)
    {
        title = title.Trim();
        if (title.Length == 0)
        {
            return false;
        }

        var temp = new TaskDto(NextTempId(), title, null, null, [], null, null, TaskStateDto.Planned, date, null, null,
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

    /// <summary>
    /// Перетаскивание в день: из ленты, из другого дня или внутри дня.
    /// Дело без времени встаёт на позицию index среди дел дня без времени (так же считает сервер);
    /// дело со временем сохраняет время и встаёт по нему — index для него не важен.
    /// false — не перенесли (некуда, в тот день уже есть занятие цели или сервер отказал).
    /// </summary>
    public async Task<bool> MoveTaskToDayAsync(int taskId, DateOnly date, int index)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || task.State == TaskStateDto.Trashed)
        {
            return false;
        }

        // Бросили туда же, где лежало, — ничего не делаем
        if (task.PlannedDate == date && task.State != TaskStateDto.Someday)
        {
            // Дело со временем стоит по времени — внутри своего дня двигать некуда
            if (task.StartTime is not null)
            {
                return false;
            }

            var current = TasksOn(date).Where(t => t.StartTime is null).ToList().FindIndex(t => t.Id == taskId);
            var count = TasksOn(date).Count(t => t.StartTime is null && t.Id != taskId);
            if (current == Math.Clamp(index, 0, count))
            {
                return false;
            }
        }

        // В другой день этой недели, где у цели уже есть занятие, — нельзя (если не разрешено несколько в день);
        // на другой неделе то же проверит сервер
        if (task.PlannedDate != date && GoalOfTask(taskId) is { AllowSeveralPerDay: false } own
            && own.WeekStart == RuDates.WeekStartOf(date)
            && own.Sessions.FirstOrDefault(s => s.PlannedDate == date && s.TaskId != taskId) is { } taken)
        {
            RefuseDayTaken(taken.TaskId);
            return false;
        }

        // Занятие цели на другую неделю: там оно станет занятием той же цели (её решает сервер) — потом перечитываем
        var otherWeekRock = task.GoalId is { } goalId
                            && (FindGoal(goalId) is not { } goal || goal.WeekStart != RuDates.WeekStartOf(date));

        PlaceInDayLocally(task, date, index);
        if (otherWeekRock)
        {
            RemoveSession(taskId);
        }
        else
        {
            SyncSession(_tasks[taskId]);
        }

        var ok = await SaveAsync(() => api.MoveTaskAsync(taskId, new DayTargetDto(date, index)));
        if (ok && otherWeekRock)
        {
            await ReloadAsync();
        }

        return ok;
    }

    /// <summary>
    /// Поле «Дата» в окне дела: любая дата (в конец дел дня без времени) или null — «без даты», обратно в ленту
    /// (в свою категорию, а если её нет — в «неразобранное»). На другую неделю — плашка «Перенесено на Пн 12 окт · Показать».
    /// </summary>
    public async Task MoveTaskToDateAsync(int taskId, DateOnly? date)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || task.PlannedDate == date)
        {
            return;
        }

        if (date is not { } day)
        {
            await MoveTaskToFeedAsync(taskId, IsFeedPlace(task.CategoryId) ? task.CategoryId : null, 0);
            return;
        }

        if (await MoveTaskToDayAsync(taskId, day, int.MaxValue) && RuDates.WeekStartOf(day) != WeekStart)
        {
            notifier.WithAction($"Перенесено на {RuDates.ShortDate(day)}", "Показать", () => GoToWeekAsync(day),
                Notifier.DefaultDuration);
        }
    }

    /// <summary>
    /// Дело, которое сейчас перетаскивают через недели (листание стрелками): при смене недели его не выбрасываем,
    /// чтобы его можно было бросить в день новой недели. null — никакое.
    /// </summary>
    public void KeepWhileDragging(int? taskId) => _draggedTaskId = taskId;

    private int? _draggedTaskId;

    /// <summary>
    /// Поставить дело в день локально (без сервера): из ленты — «запланировано» и без времени;
    /// без времени — на позицию index среди дел дня без времени; со временем — по времени.
    /// </summary>
    private void PlaceInDayLocally(TaskDto task, DateOnly date, int index)
    {
        var fromFeed = task.State == TaskStateDto.Someday;
        var moved = task with
        {
            State = fromFeed ? TaskStateDto.Planned : task.State,
            PlannedDate = date,
            StartTime = fromFeed ? null : task.StartTime,
        };

        if (moved.StartTime is not null)
        {
            _tasks[task.Id] = moved;
            return;
        }

        var untimed = TasksOn(date).Where(t => t.StartTime is null && t.Id != task.Id).ToList();
        untimed.Insert(Math.Clamp(index, 0, untimed.Count), moved);
        for (var i = 0; i < untimed.Count; i++)
        {
            _tasks[untimed[i].Id] = untimed[i] with { SortOrder = i };
        }
    }

    /// <summary>Отметка «выполнено»; у цели этого занятия пересчитывается прогресс.</summary>
    public async Task SetDoneAsync(int taskId, bool isDone)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task)
        {
            return;
        }

        _tasks[taskId] = task with { State = isDone ? TaskStateDto.Done : TaskStateDto.Planned };
        SyncSession(_tasks[taskId]);
        await SaveAsync(() => api.SetTaskDoneAsync(taskId, isDone));
    }

    /// <summary>Сохранить окно редактирования. startTime = null — без времени; roleIds пусто — без ролей.</summary>
    public async Task UpdateTaskAsync(
        int taskId, string title, string? notes, IReadOnlyList<int> roleIds, int? quadrant, TimeOnly? startTime, int? durationMinutes)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        // У занятия цели роль и квадрант всегда от цели (так же решает сервер)
        if (GoalOfTask(taskId) is { } goal)
        {
            roleIds = [goal.RoleId];
            quadrant = goal.Quadrant;
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
            RoleIds = [.. roleIds.Distinct().Order()],
            Quadrant = quadrant,
            StartTime = startTime,
            DurationMinutes = durationMinutes,
            SortOrder = sortOrder,
        };

        await SaveAsync(() => api.UpdateTaskAsync(taskId,
            new UpdateTaskRequest(title, notes, roleIds, quadrant, startTime, durationMinutes)));
    }

    private int NextUntimedOrder(DateOnly date)
    {
        var untimed = TasksOn(date).Where(t => t.StartTime is null).ToList();
        return untimed.Count == 0 ? 0 : untimed.Max(t => t.SortOrder) + 1;
    }
}
