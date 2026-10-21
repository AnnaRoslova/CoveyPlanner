using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>
/// Состояние экрана на клиенте: лента, роли, текущая неделя.
/// Компоненты читают данные отсюда и подписываются на <see cref="Changed"/>.
/// Изменения делаются оптимистично: сначала локально, затем на сервере;
/// при ошибке показывается сообщение и состояние перезагружается с сервера.
/// Операции разнесены по файлам: PlannerState.Someday.cs — лента и корзина.
/// </summary>
public partial class PlannerState(PlannerApi api, Notifier notifier)
{
    private readonly Dictionary<int, TaskDto> _tasks = [];

    // Отрицательные Id — у записей, которые ещё сохраняются на сервере
    private int _nextTempId = -1;

    public event Action? Changed;

    public bool IsLoaded { get; private set; }

    public string? LoadError { get; private set; }

    public IReadOnlyList<string> RolePalette { get; private set; } = [];

    public int RecommendedGoalsPerWeek { get; private set; } = 3;

    public List<RoleDto> Roles { get; private set; } = [];

    public List<CategoryDto> Categories { get; private set; } = [];

    public List<WeeklyGoalDto> Goals { get; private set; } = [];

    public int TrashCount { get; private set; }

    public DateOnly WeekStart { get; private set; } = RuDates.WeekStartOf(RuDates.Today);

    public IEnumerable<DateOnly> WeekDays => Enumerable.Range(0, 7).Select(WeekStart.AddDays);

    // Запросы для компонентов

    public IEnumerable<RoleDto> SawRoles => Roles.Where(r => r.IsSharpenSaw);

    public IEnumerable<RoleDto> UserRoles => Roles.Where(r => !r.IsSharpenSaw).OrderBy(r => r.SortOrder);

    public RoleDto? FindRole(int? roleId) => roleId is { } id ? Roles.FirstOrDefault(r => r.Id == id) : null;

    /// <summary>Все роли в порядке экрана: сначала измерения пилы, затем пользовательские.</summary>
    public IEnumerable<RoleDto> OrderedRoles => Roles.OrderBy(r => r.IsSharpenSaw ? 0 : 1).ThenBy(r => r.SortOrder);

    /// <summary>Роли дела в порядке экрана (первая — цвет чекбокса).</summary>
    public IReadOnlyList<RoleDto> RolesOf(TaskDto task) => OrderedRoles.Where(r => task.RoleIds.Contains(r.Id)).ToList();

    public IEnumerable<CategoryDto> OrderedCategories => Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id);

    /// <summary>Мысли ленты в категории; categoryId = null — «неразобранное». Проекты в ленте не показываются (N6.2).</summary>
    public IEnumerable<TaskDto> SomedayIn(int? categoryId) =>
        _tasks.Values.Where(t => t.State == TaskStateDto.Someday && !t.IsProject && t.CategoryId == categoryId).OrderBy(t => t.SortOrder);

    public int SomedayCount(int? categoryId) =>
        _tasks.Values.Count(t => t.State == TaskStateDto.Someday && !t.IsProject && t.CategoryId == categoryId);

    /// <summary>Дела дня: сначала со временем (по времени), затем без времени (ручной порядок).</summary>
    public IEnumerable<TaskDto> TasksOn(DateOnly date) =>
        _tasks.Values
            .Where(t => (t.State is TaskStateDto.Planned or TaskStateDto.Done) && t.PlannedDate == date)
            .OrderBy(t => t.StartTime is null)
            .ThenBy(t => t.StartTime)
            .ThenBy(t => t.SortOrder);

    /// <summary>
    /// Дела дня, как их видно (docs/specs/ui-declutter.md, D3): невыполненные (со временем — по времени, потом ручной порядок),
    /// под ними — выполненные в том же порядке. Ручной порядок (<see cref="TasksOn"/>) не меняется: сняли отметку — дело на своём месте.
    /// </summary>
    public IEnumerable<TaskDto> DayRows(DateOnly date) =>
        TasksOn(date).OrderBy(t => t.State == TaskStateDto.Done);

    /// <summary>
    /// Бросок в день (D3): позиция на экране — среди невыполненных дел без времени (выполненные внизу в счёт не идут).
    /// Переводит её в позицию сервера — среди всех дел дня без времени по ручному порядку. Ниже последнего невыполненного —
    /// сразу после него (выше выполненных, они всё равно рисуются внизу).
    /// </summary>
    public int DropIndexInDay(DateOnly date, int taskId, int visualIndex)
    {
        var untimed = TasksOn(date).Where(t => t.StartTime is null && t.Id != taskId).ToList();
        var open = untimed.Where(t => t.State != TaskStateDto.Done).ToList();
        if (open.Count == untimed.Count)
        {
            return visualIndex; // выполненных без времени нет — позиции совпадают
        }

        return visualIndex < open.Count
            ? untimed.IndexOf(open[Math.Max(0, visualIndex)])
            : open.Count == 0 ? 0 : untimed.IndexOf(open[^1]) + 1;
    }

    /// <summary>
    /// Дела показанной недели с этой ролью, кроме занятий целей («ещё дела» в левой панели):
    /// по дням, в дне — как в списке дня.
    /// </summary>
    public IReadOnlyList<TaskDto> OtherTasksOf(int roleId) =>
        WeekDays.SelectMany(TasksOn).Where(t => t.RoleIds.Contains(roleId) && !IsRock(t.Id)).ToList();

    public IEnumerable<WeeklyGoalDto> GoalsFor(int roleId) =>
        Goals.Where(g => g.HasRole(roleId)).OrderBy(g => g.SortOrderIn(roleId));

    /// <summary>Дело — занятие цели недели, «большой камень».</summary>
    public bool IsRock(int taskId) => GoalOfTask(taskId) is not null;

    public int RocksDone => Goals.Count(g => g.IsDone);

    public int RocksTotal => Goals.Count;

    /// <summary>Занятие, которое сейчас коротко подсвечено (в этот день у цели оно уже есть).</summary>
    public int? FlashTaskId { get; private set; }

    /// <summary>
    /// В этот день у цели уже есть занятие (а несколько в день не разрешено): ничего не делаем,
    /// существующее занятие коротко подсвечиваем, подсказка «уже есть в этот день».
    /// </summary>
    private void RefuseDayTaken(int? existingTaskId)
    {
        notifier.Info("уже есть в этот день");
        if (existingTaskId is not { } id)
        {
            return;
        }

        FlashTaskId = id;
        NotifyChanged();
        _ = UnflashLaterAsync(id);
    }

    private async Task UnflashLaterAsync(int taskId)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        if (FlashTaskId == taskId)
        {
            FlashTaskId = null;
            NotifyChanged();
        }
    }

    /// <summary>Отмеченные занятия целей недели (сверх «раз в неделю» не считаются).</summary>
    public int SessionsDone => Goals.Sum(g => Math.Min(g.DoneCount, g.TimesPerWeek));

    /// <summary>Сколько всего занятий нужно по «раз в неделю» у целей недели.</summary>
    public int SessionsTotal => Goals.Sum(g => g.TimesPerWeek);

    // Загрузка и навигация

    public async Task LoadAsync()
    {
        try
        {
            var boardTask = api.GetBoardAsync();
            var weekTask = api.GetWeekAsync(WeekStart);
            var habitsTask = api.GetHabitsAsync(RuDates.Today);
            ApplyBoard(await boardTask);
            ApplyWeek(await weekTask);
            ApplyHabits(await habitsTask);
            IsLoaded = true;
            LoadError = null;
        }
        catch (ApiException ex)
        {
            LoadError = ex.Message;
        }

        NotifyChanged();
    }

    public async Task GoToWeekAsync(DateOnly anyDate)
    {
        await CommitPendingDeleteAsync();
        var weekStart = RuDates.WeekStartOf(anyDate);
        WeekStart = weekStart;
        NotifyChanged();
        try
        {
            var week = await api.GetWeekAsync(weekStart);

            // Пока ждали ответа, могли перелистнуть дальше — старый ответ не нужен
            if (WeekStart == weekStart)
            {
                ApplyWeek(week);
            }
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    public Task NextWeekAsync() => GoToWeekAsync(WeekStart.AddDays(7));

    public Task PreviousWeekAsync() => GoToWeekAsync(WeekStart.AddDays(-7));

    public Task TodayAsync() => GoToWeekAsync(RuDates.Today);

    /// <summary>
    /// Наступил новый день (см. <see cref="TodayWatcher"/>). Если на экране была текущая неделя,
    /// а новый день уже в следующей, — листаем на неё; другую неделю, которую смотрят, не трогаем.
    /// </summary>
    public async Task OnTodayChangedAsync(DateOnly previous, DateOnly today)
    {
        _ = RefreshHabitsAsync(); // новая неделя или месяц — у копилок новые итоги
        _ = CheckStaleAsync(); // за ночь мысли могли перешагнуть срок (BS4)
        var wasCurrentWeek = WeekStart == RuDates.WeekStartOf(previous);
        if (wasCurrentWeek && RuDates.WeekStartOf(today) != WeekStart)
        {
            await GoToWeekAsync(today);
        }
        else
        {
            NotifyChanged(); // перерисовать подсветку «сегодня» и кнопку «Сегодня»
        }
    }

    private void ApplyBoard(BoardDto board)
    {
        RolePalette = board.RolePalette;
        RecommendedGoalsPerWeek = board.RecommendedGoalsPerWeek;
        Roles = [.. board.Roles];
        Categories = [.. board.Categories];
        TrashCount = board.TrashCount;
        StaleMonths = board.StaleMonths;
        Mission = board.Settings?.Mission;
        ShowMissionInPanel = board.Settings?.ShowMissionInPanel ?? false;

        // Лента, открытые проекты плана любой недели («🛠 Проекты», N6.9) и их шаги
        foreach (var id in _tasks.Values.Where(IsBoardTask).Select(t => t.Id).ToList())
        {
            _tasks.Remove(id);
        }

        foreach (var task in board.SomedayTasks.Concat(board.PlannedProjects ?? []).Concat(board.Steps ?? []))
        {
            _tasks[task.Id] = task;
        }
    }

    /// <summary>
    /// Дело приходит с доской, а не с неделей: мысль или проект ленты, открытый проект плана (любой недели) и шаги таких проектов.
    /// </summary>
    private bool IsBoardTask(TaskDto task) =>
        task.State == TaskStateDto.Someday
        || task is { IsProject: true, State: TaskStateDto.Planned, ParentId: null }
        || (task.ParentId is { } parentId && _tasks.GetValueOrDefault(parentId) is { } parent && parent.ParentId is null
            && (parent.State == TaskStateDto.Someday || parent is { IsProject: true, State: TaskStateDto.Planned }));

    private void ApplyWeek(WeekDto week)
    {
        // Дела плана храним только для показанной недели (и дело, которое перетаскивают через недели);
        // дела доски (лента, проекты и их шаги) остаются — они не от недели
        foreach (var id in _tasks.Values
                     .Where(t => !IsBoardTask(t) && t.Id != _draggedTaskId)
                     .Select(t => t.Id).ToList())
        {
            _tasks.Remove(id);
        }

        // Шаги видимых проектов — раньше дел дня: шаг, стоящий в дне, приходит в обоих списках
        foreach (var task in (week.Steps ?? []).Concat(week.Tasks))
        {
            _tasks[task.Id] = task;
        }

        Goals = [.. week.Goals];
    }

    /// <summary>
    /// Отправить изменение на сервер после того, как оно уже применено локально.
    /// При ошибке — сообщение и перезагрузка всего состояния с сервера (откат).
    /// </summary>
    private async Task<bool> SaveAsync(Func<Task> serverCall)
    {
        NotifyChanged();
        try
        {
            // Отложенное удаление — на сервер раньше этого изменения; перечитываем после него,
            // иначе перечитанное затёрло бы только что сделанное на экране
            var reload = await CommitPendingDeleteAsync(reload: false);
            await serverCall();
            if (reload)
            {
                await ReloadAsync();
            }

            NotifyChanged();
            return true;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            await ReloadAsync();
            return false;
        }
    }

    /// <summary>Перечитать всё с сервера, не показывая экран загрузки.</summary>
    private async Task ReloadAsync()
    {
        await CommitPendingDeleteAsync(reload: false);
        try
        {
            var boardTask = api.GetBoardAsync();
            var weekTask = api.GetWeekAsync(WeekStart);
            var habitsTask = api.GetHabitsAsync(RuDates.Today);
            ApplyBoard(await boardTask);
            ApplyWeek(await weekTask);
            ApplyHabits(await habitsTask);
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    private int NextTempId() => _nextTempId--;

    private void NotifyChanged() => Changed?.Invoke();
}
