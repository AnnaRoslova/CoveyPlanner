using Planner.Shared;

namespace Planner.Client.Services;

// «🛠 Проекты» (docs/specs/next-batch.md, N6): проект — дело с флагом IsProject; в ленте его нет, он живёт в панели проектов.
// Сделать проектом — кнопкой или первым «+ шаг», перестать — только явно и без шагов.
public partial class PlannerState
{
    /// <summary>Попросили показать «🛠 Проекты» (плашка, кнопка) — на этом проекте или просто панель (null).</summary>
    public event Action<int?>? ProjectsRequested;

    public void RequestProjects(int? projectId = null) => ProjectsRequested?.Invoke(projectId);

    /// <summary>
    /// Открытые проекты (N6.9): без родителя, не выполнены и не в корзине — из ленты и из плана любой недели.
    /// categoryKey: null — все категории, <see cref="DragDropService.UnsortedKey"/> — без категории, иначе Id категории.
    /// </summary>
    public IReadOnlyList<TaskDto> OpenProjects() =>
        _tasks.Values
            .Where(t => t is { IsProject: true, ParentId: null } && t.State is TaskStateDto.Someday or TaskStateDto.Planned)
            .ToList();

    /// <summary>Проекты группы по порядку: сначала без дня (в порядке ленты), потом с днём — по дате (N6.9).</summary>
    public IReadOnlyList<TaskDto> ProjectsIn(int? categoryId, string? query = null) =>
        OpenProjects()
            .Where(t => t.CategoryId == categoryId && (string.IsNullOrEmpty(query) || FeedMatches(t, query)))
            .OrderBy(t => t.PlannedDate is not null)
            .ThenBy(t => t.PlannedDate)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToList();

    /// <summary>Сколько проектов нашёл бы поиск ленты (строка «🛠 в проектах: N», N6.12).</summary>
    public int ProjectsMatching(string query) => OpenProjects().Count(t => t.Id > 0 && FeedMatches(t, query));

    /// <summary>Коротко подсветить запись (найти её глазами: проект после «🛠 Проекты» на плашке).</summary>
    public void Flash(int taskId)
    {
        FlashTaskId = taskId;
        NotifyChanged();
        _ = UnflashLaterAsync(taskId);
    }

    /// <summary>«+ проект» (N6.11): проект без дня наверху своей категории (null — без категории). false — не сохранился.</summary>
    public async Task<bool> CreateProjectAsync(string title, int? categoryId)
    {
        title = title.Trim();
        if (title.Length == 0 || !IsFeedPlace(categoryId))
        {
            return false;
        }

        var temp = new TaskDto(NextTempId(), title, null, categoryId, [], null, null, TaskStateDto.Someday, null, null, null,
            SortOrder: int.MinValue, TrashedAt: null, IsFromSomeday: true, IsProject: true);
        _tasks[temp.Id] = temp;
        RenumberSomeday(categoryId);

        var ok = await SaveAsync(async () =>
        {
            var saved = await api.CreateTaskAsync(title, new SomedayTargetDto(categoryId, 0), isProject: true);
            ReplaceTempTask(temp.Id, saved);
        });

        if (!ok && _tasks.Remove(temp.Id))
        {
            NotifyChanged();
        }

        return ok;
    }

    /// <summary>
    /// «🛠 Сделать проектом» (N6.3): мысль уходит из ленты в «🛠 Проекты» (дело в дне остаётся в дне, с 🛠).
    /// Плашка «„…“ теперь проект · 🛠 Проекты · Вернуть».
    /// </summary>
    public async Task MakeProjectAsync(int taskId)
    {
        if (taskId < 0 || FindTask(taskId) is not { IsProject: false } task)
        {
            return;
        }

        _tasks[taskId] = task with { IsProject = true };
        if (!await SaveAsync(async () => _tasks[taskId] = await api.SetProjectAsync(taskId, true)))
        {
            return;
        }

        OfferProjectUndo(task, firstStepId: null);
    }

    /// <summary>
    /// Плашка «теперь проект» (N6.3, N6.14): «🛠 Проекты» — панель на этом проекте; «Вернуть» — снова не проект
    /// (если проектом сделал первый «+ шаг» — этот шаг удаляется: одно действие — одна отмена).
    /// </summary>
    private void OfferProjectUndo(TaskDto task, int? firstStepId)
    {
        notifier.WithActions($"«{task.Title}» теперь проект",
            "🛠 Проекты", () =>
            {
                RequestProjects(task.Id);
                return Task.CompletedTask;
            },
            "Вернуть", () => UndoMakeProjectAsync(task.Id, firstStepId),
            UndoWindow, nearTrash: task.State == TaskStateDto.Someday);
    }

    private async Task UndoMakeProjectAsync(int taskId, int? firstStepId)
    {
        await SaveAsync(async () =>
        {
            if (firstStepId is { } stepId)
            {
                await api.TrashTaskAsync(stepId);
                await api.DeleteForeverAsync(stepId);
            }

            await api.SetProjectAsync(taskId, false);
        });
        await ReloadAsync();
    }

    /// <summary>
    /// «🛠 больше не проект» (N6.7): только у проекта без шагов (корзина не в счёт). Проект без дня — снова мысль ленты.
    /// </summary>
    public async Task UnmakeProjectAsync(int taskId)
    {
        if (taskId < 0 || FindTask(taskId) is not { IsProject: true } task || StepsOf(taskId).Count > 0)
        {
            return;
        }

        _tasks[taskId] = task with { IsProject = false };
        if (await SaveAsync(async () => _tasks[taskId] = await api.SetProjectAsync(taskId, false)))
        {
            await ReloadAsync();
        }
    }

    /// <summary>Проект перетащили в группу панели «🛠 Проекты»: снять с дня (если стоял) и положить в эту категорию.</summary>
    public async Task MoveProjectToGroupAsync(int taskId, int? categoryId)
    {
        if (FindTask(taskId) is not { IsProject: true, ParentId: null } task
            || (task.State == TaskStateDto.Someday && task.CategoryId == categoryId))
        {
            return; // не проект или туда же, где лежал (ручного порядка в панели нет)
        }

        await MoveTaskToFeedAsync(taskId, categoryId, 0);
    }
}
