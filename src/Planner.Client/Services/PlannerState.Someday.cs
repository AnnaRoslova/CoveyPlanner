using Planner.Shared;

namespace Planner.Client.Services;

// Лента «Когда-нибудь»: категории, дела ленты, корзина
public partial class PlannerState
{
    public CategoryDto? FindCategory(int? categoryId) =>
        categoryId is { } id ? Categories.FirstOrDefault(c => c.Id == id) : null;

    /// <summary>Есть ли такое место в ленте: категория или «неразобранное» (null).</summary>
    private bool IsFeedPlace(int? categoryId) => categoryId is null || FindCategory(categoryId) is not null;

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

    /// <summary>Раскрыта ли хоть одна категория — тогда общая кнопка «свернуть всё».</summary>
    public bool AnyCategoryExpanded => Categories.Any(c => c.IsExpanded);

    /// <summary>Раскрыть или свернуть все категории разом.</summary>
    public async Task SetAllCategoriesExpandedAsync(bool expanded)
    {
        for (var i = 0; i < Categories.Count; i++)
        {
            Categories[i] = Categories[i] with { IsExpanded = expanded };
        }

        NotifyChanged();
        await SaveAsync(() => api.SetAllCategoriesExpandedAsync(expanded));
    }

    /// <summary>Новая категория — в конец или в начало (atTop). Ждём сервер — нужен настоящий Id.</summary>
    public async Task<CategoryDto?> CreateCategoryAsync(string name, string? emoji, bool atTop = false)
    {
        try
        {
            var created = await api.CreateCategoryAsync(name.Trim(), emoji, atTop);
            Categories.Add(created);
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

    /// <summary>
    /// Удалить категорию. Её дела: keepTasks — наверх «неразобранного» в том же порядке (так же делает сервер),
    /// иначе — в корзину.
    /// </summary>
    public async Task DeleteCategoryAsync(int categoryId, bool keepTasks)
    {
        var tasks = SomedayIn(categoryId).ToList();
        Categories.RemoveAll(c => c.Id == categoryId);
        if (keepTasks)
        {
            var order = 0;
            foreach (var task in tasks.Concat(SomedayIn(null).ToList()))
            {
                _tasks[task.Id] = task with { CategoryId = null, SortOrder = order++ };
            }
        }
        else
        {
            foreach (var task in tasks)
            {
                _tasks.Remove(task.Id);
            }

            TrashCount += tasks.Count;
        }

        await SaveAsync(() => api.DeleteCategoryAsync(categoryId, keepTasks));
    }

    // ---------- Дела ленты ----------

    /// <summary>
    /// Новое дело в ленте: «Записать мысль» кладёт наверх категории (null — «неразобранного»),
    /// «+ добавить» внизу категории — в конец. false — не сохранилось.
    /// </summary>
    public async Task<bool> CreateSomedayTaskAsync(string title, int? categoryId, bool atEnd = false)
    {
        title = title.Trim();
        if (title.Length == 0 || !IsFeedPlace(categoryId))
        {
            return false;
        }

        var temp = new TaskDto(NextTempId(), title, null, categoryId, null, null, null, TaskStateDto.Someday, null, null, null,
            SortOrder: atEnd ? int.MaxValue : int.MinValue, TrashedAt: null);
        _tasks[temp.Id] = temp;
        RenumberSomeday(categoryId);

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

    /// <summary>
    /// Перетаскивание в ленту: из другой категории (или «неразобранного», categoryId = null), внутри категории или из плана.
    /// Дело из плана возвращается без даты, времени и отметки «выполнено», связь с целью разрывается.
    /// index — позиция среди дел категории без самого перетаскиваемого (так же считает сервер).
    /// </summary>
    public async Task MoveTaskToFeedAsync(int taskId, int? categoryId, int index)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || task.State == TaskStateDto.Trashed
            || !IsFeedPlace(categoryId))
        {
            return;
        }

        var wasSomeday = task.State == TaskStateDto.Someday;

        // Бросили туда же, где лежало, — ничего не делаем
        if (wasSomeday && task.CategoryId == categoryId
            && SomedayIn(categoryId).ToList().FindIndex(t => t.Id == taskId)
               == Math.Clamp(index, 0, SomedayCount(categoryId) - 1))
        {
            return;
        }

        PlaceInFeedLocally(task, categoryId, index);
        if (task.GoalId is not null)
        {
            // Сервер тоже разрывает связь с целью при возврате в ленту
            _tasks[taskId] = _tasks[taskId] with { GoalId = null };
            RemoveSession(taskId);
        }

        await SaveAsync(() => api.MoveTaskAsync(taskId, new SomedayTargetDto(categoryId, index)));
    }

    /// <summary>
    /// Поставить дело в категорию ленты локально (без сервера): без даты, времени и отметки,
    /// на позицию index среди дел категории без него самого.
    /// </summary>
    private void PlaceInFeedLocally(TaskDto task, int? categoryId, int index)
    {
        var wasInFeed = task.State == TaskStateDto.Someday;
        var oldCategoryId = task.CategoryId;
        var siblings = SomedayIn(categoryId).Where(t => t.Id != task.Id).ToList();
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), task with
        {
            State = TaskStateDto.Someday,
            CategoryId = categoryId,
            PlannedDate = null,
            StartTime = null,
        });
        for (var i = 0; i < siblings.Count; i++)
        {
            _tasks[siblings[i].Id] = siblings[i] with { SortOrder = i };
        }

        if (wasInFeed && oldCategoryId != categoryId)
        {
            RenumberSomeday(oldCategoryId);
        }
    }

    /// <summary>Удалить дело — в корзину. Занятие цели перестаёт считаться в её прогрессе.</summary>
    public async Task TrashTaskAsync(int taskId)
    {
        if (taskId < 0 || !_tasks.Remove(taskId))
        {
            return;
        }

        TrashCount++;
        RemoveSession(taskId);
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
    /// Вернуть из корзины на прежнее место. Дело ленты без своей категории вернётся в «неразобранное».
    /// false — не получилось (сообщение уже показано).
    /// </summary>
    public async Task<bool> RestoreFromTrashAsync(int taskId)
    {
        TaskDto restored;
        try
        {
            restored = await api.RestoreTaskAsync(taskId);
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return false;
        }

        TrashCount = Math.Max(0, TrashCount - 1);
        if (restored.GoalId is not null)
        {
            await ReloadAsync(); // занятие цели снова считается — прогресс цели берём с сервера
        }
        else if (restored.State == TaskStateDto.Someday)
        {
            // Сервер поставил дело наверх категории (или «неразобранного») — повторяем порядок локально
            _tasks[restored.Id] = restored with { SortOrder = int.MinValue };
            RenumberSomeday(restored.CategoryId);
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
        return true;
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
    private void RenumberSomeday(int? categoryId)
    {
        var order = 0;
        foreach (var task in SomedayIn(categoryId).ToList())
        {
            _tasks[task.Id] = task with { SortOrder = order++ };
        }
    }
}
