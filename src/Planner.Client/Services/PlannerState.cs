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

    /// <summary>Дела ленты в категории; categoryId = null — «неразобранное».</summary>
    public IEnumerable<TaskDto> SomedayIn(int? categoryId) =>
        _tasks.Values.Where(t => t.State == TaskStateDto.Someday && t.CategoryId == categoryId).OrderBy(t => t.SortOrder);

    public int SomedayCount(int? categoryId) =>
        _tasks.Values.Count(t => t.State == TaskStateDto.Someday && t.CategoryId == categoryId);

    /// <summary>Дела дня: сначала со временем (по времени), затем без времени (ручной порядок).</summary>
    public IEnumerable<TaskDto> TasksOn(DateOnly date) =>
        _tasks.Values
            .Where(t => (t.State is TaskStateDto.Planned or TaskStateDto.Done) && t.PlannedDate == date)
            .OrderBy(t => t.StartTime is null)
            .ThenBy(t => t.StartTime)
            .ThenBy(t => t.SortOrder);

    /// <summary>
    /// Дела показанной недели с этой ролью, кроме занятий целей («ещё дела» в левой панели):
    /// по дням, в дне — как в списке дня.
    /// </summary>
    public IReadOnlyList<TaskDto> OtherTasksOf(int roleId) =>
        WeekDays.SelectMany(TasksOn).Where(t => t.RoleIds.Contains(roleId) && !IsRock(t.Id)).ToList();

    public IEnumerable<WeeklyGoalDto> GoalsFor(int roleId) =>
        Goals.Where(g => g.RoleId == roleId).OrderBy(g => g.SortOrder);

    /// <summary>Дело — занятие цели недели, «большой камень».</summary>
    public bool IsRock(int taskId) => GoalOfTask(taskId) is not null;

    public int RocksDone => Goals.Count(g => g.IsDone);

    public int RocksTotal => Goals.Count;

    // Загрузка и навигация

    public async Task LoadAsync()
    {
        try
        {
            var boardTask = api.GetBoardAsync();
            var weekTask = api.GetWeekAsync(WeekStart);
            ApplyBoard(await boardTask);
            ApplyWeek(await weekTask);
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

        foreach (var id in _tasks.Values.Where(t => t.State == TaskStateDto.Someday).Select(t => t.Id).ToList())
        {
            _tasks.Remove(id);
        }

        foreach (var task in board.SomedayTasks)
        {
            _tasks[task.Id] = task;
        }
    }

    private void ApplyWeek(WeekDto week)
    {
        // Дела плана храним только для показанной недели (и дело, которое перетаскивают через недели)
        foreach (var id in _tasks.Values.Where(t => t.State != TaskStateDto.Someday && t.Id != _draggedTaskId).Select(t => t.Id).ToList())
        {
            _tasks.Remove(id);
        }

        foreach (var task in week.Tasks)
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
            ApplyBoard(await boardTask);
            ApplyWeek(await weekTask);
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
