using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>Чем закончилось восстановление из корзины.</summary>
public enum RestoreResult
{
    Restored,

    /// <summary>Своей категории у дела нет — нужно выбрать и повторить.</summary>
    NeedsCategory,

    Failed,
}

// Лента «Когда-нибудь»: категории, дела ленты, корзина
public partial class PlannerState
{
    public CategoryDto? FindCategory(int? categoryId) =>
        categoryId is { } id ? Categories.FirstOrDefault(c => c.Id == id) : null;

    // ---------- Категории ----------

    /// <summary>Раскрыть/свернуть категорию; состояние запоминается на сервере.</summary>
    public async Task ToggleCategoryAsync(int categoryId)
    {
        if (FindCategory(categoryId) is not { } category)
        {
            return;
        }

        var expanded = !category.IsExpanded;
        ReplaceCategory(category with { IsExpanded = expanded });
        await SaveAsync(() => api.UpdateCategoryAsync(categoryId, new UpdateCategoryRequest(null, null, expanded)));
    }

    /// <summary>Новая категория (в конец). Ждём сервер — нужен настоящий Id.</summary>
    public async Task<CategoryDto?> CreateCategoryAsync(string name, string? emoji)
    {
        try
        {
            var created = await api.CreateCategoryAsync(name.Trim(), emoji);
            Categories.Add(created);
            LastUsedCategoryId ??= created.Id;
            NotifyChanged();
            return created;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return null;
        }
    }

    /// <summary>Переименовать и/или сменить эмодзи. emoji = "" — убрать иконку.</summary>
    public async Task UpdateCategoryAsync(int categoryId, string name, string emoji)
    {
        if (FindCategory(categoryId) is not { } category)
        {
            return;
        }

        ReplaceCategory(category with { Name = name.Trim(), Emoji = emoji.Length == 0 ? null : emoji });
        await SaveAsync(() => api.UpdateCategoryAsync(categoryId, new UpdateCategoryRequest(name.Trim(), emoji, null)));
    }

    /// <summary>Удалить категорию; её дела уходят в корзину.</summary>
    public async Task DeleteCategoryAsync(int categoryId)
    {
        var tasks = SomedayIn(categoryId).ToList();
        Categories.RemoveAll(c => c.Id == categoryId);
        foreach (var task in tasks)
        {
            _tasks.Remove(task.Id);
        }

        TrashCount += tasks.Count;
        if (LastUsedCategoryId == categoryId)
        {
            LastUsedCategoryId = OrderedCategories.FirstOrDefault()?.Id;
        }

        await SaveAsync(() => api.DeleteCategoryAsync(categoryId));
    }

    // ---------- Дела ленты ----------

    /// <summary>
    /// Новое дело в ленте: «Записать мысль» кладёт наверх категории, «+ добавить» внизу категории — в конец.
    /// false — не сохранилось.
    /// </summary>
    public async Task<bool> CreateSomedayTaskAsync(string title, int categoryId, bool atEnd = false)
    {
        title = title.Trim();
        if (title.Length == 0 || FindCategory(categoryId) is null)
        {
            return false;
        }

        var temp = new TaskDto(NextTempId(), title, null, categoryId, null, null, TaskStateDto.Someday, null, null,
            SortOrder: atEnd ? int.MaxValue : int.MinValue, TrashedAt: null);
        _tasks[temp.Id] = temp;
        RenumberSomeday(categoryId);
        LastUsedCategoryId = categoryId;

        // Позиция на сервере: 0 — наверх, «очень большая» — в конец (сервер прижимает к краю)
        var index = atEnd ? int.MaxValue : 0;

        var ok = await SaveAsync(async () =>
        {
            var saved = await api.CreateTaskAsync(title, new SomedayTargetDto(categoryId, index));
            ReplaceTempTask(temp.Id, saved);
        });

        // Если сервер недоступен, перезагрузка тоже не удалась — убираем несохранённую запись сами
        if (!ok && _tasks.Remove(temp.Id))
        {
            NotifyChanged();
        }

        return ok;
    }

    /// <summary>Переименовать дело (остальные поля не меняются).</summary>
    public async Task RenameTaskAsync(int taskId, string title)
    {
        title = title.Trim();
        if (taskId < 0 || !_tasks.TryGetValue(taskId, out var task) || title.Length == 0 || title == task.Title)
        {
            return;
        }

        _tasks[taskId] = task with { Title = title };
        await SaveAsync(() => api.UpdateTaskAsync(taskId,
            new UpdateTaskRequest(title, task.Notes, task.RoleId, task.Quadrant, task.StartTime)));
    }

    /// <summary>Удалить дело — в корзину. Связанная цель остаётся без дела.</summary>
    public async Task TrashTaskAsync(int taskId)
    {
        if (taskId < 0 || !_tasks.Remove(taskId))
        {
            return;
        }

        TrashCount++;
        UnlinkGoalsFrom(taskId);
        await SaveAsync(() => api.TrashTaskAsync(taskId));
    }

    // ---------- Корзина ----------

    public async Task<List<TaskDto>> GetTrashAsync()
    {
        try
        {
            return await api.GetTrashAsync();
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Вернуть из корзины на прежнее место. Если своей категории у дела нет —
    /// <see cref="RestoreResult.NeedsCategory"/>: спросить категорию и вызвать ещё раз с categoryId.
    /// </summary>
    public async Task<RestoreResult> RestoreFromTrashAsync(int taskId, int? categoryId = null)
    {
        TaskDto restored;
        try
        {
            restored = await api.RestoreTaskAsync(taskId, categoryId);
        }
        catch (ApiException ex) when (ex.IsCategoryRequired)
        {
            return RestoreResult.NeedsCategory;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return RestoreResult.Failed;
        }

        TrashCount = Math.Max(0, TrashCount - 1);
        if (restored.State == TaskStateDto.Someday)
        {
            // Сервер поставил дело наверх категории — повторяем порядок локально
            _tasks[restored.Id] = restored with { SortOrder = int.MinValue };
            RenumberSomeday(restored.CategoryId!.Value);
        }
        else if (restored.PlannedDate is { } date)
        {
            if (WeekDays.Contains(date))
            {
                await ReloadAsync(); // порядок в дне пересчитал сервер — берём его
            }
            else
            {
                notifier.Info($"Дело вернулось в план: {RuDates.ShortDay(date)}, {RuDates.DayMonth(date)}");
            }
        }

        NotifyChanged();
        return RestoreResult.Restored;
    }

    public async Task DeleteForeverAsync(int taskId)
    {
        TrashCount = Math.Max(0, TrashCount - 1);
        await SaveAsync(() => api.DeleteForeverAsync(taskId));
    }

    public async Task EmptyTrashAsync()
    {
        TrashCount = 0;
        await SaveAsync(api.EmptyTrashAsync);
    }

    // ---------- Помощники ----------

    private void ReplaceCategory(CategoryDto category)
    {
        var index = Categories.FindIndex(c => c.Id == category.Id);
        if (index >= 0)
        {
            Categories[index] = category;
        }
    }

    /// <summary>
    /// Заменить временное дело сохранённым. Порядок оставляем локальный
    /// (сервер считает его так же), чтобы запись не «прыгала».
    /// </summary>
    private void ReplaceTempTask(int tempId, TaskDto saved)
    {
        var sortOrder = _tasks.TryGetValue(tempId, out var temp) ? temp.SortOrder : saved.SortOrder;
        _tasks.Remove(tempId);
        _tasks[saved.Id] = saved with { SortOrder = sortOrder };
    }

    /// <summary>Проставить SortOrder = 0..n-1 делам категории в текущем порядке.</summary>
    private void RenumberSomeday(int categoryId)
    {
        var order = 0;
        foreach (var task in SomedayIn(categoryId).ToList())
        {
            _tasks[task.Id] = task with { SortOrder = order++ };
        }
    }

    private void UnlinkGoalsFrom(int taskId)
    {
        for (var i = 0; i < Goals.Count; i++)
        {
            if (Goals[i].TaskId == taskId)
            {
                Goals[i] = Goals[i] with { TaskId = null, PlannedDate = null, StartTime = null };
            }
        }
    }
}
