using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>
/// Состояние экрана на клиенте: лента, роли, текущая неделя.
/// Компоненты читают данные отсюда и подписываются на <see cref="Changed"/>.
/// Изменения делаются оптимистично: сначала локально, затем на сервере;
/// при ошибке состояние перезагружается с сервера.
/// </summary>
public class PlannerState(PlannerApi api)
{
    private readonly Dictionary<int, TaskDto> _tasks = [];

    public event Action? Changed;

    public bool IsLoaded { get; private set; }

    public string? LoadError { get; private set; }

    public SettingsDto Settings { get; private set; } = new(7, 23, 30);

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

    public IEnumerable<CategoryDto> OrderedCategories => Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id);

    public IEnumerable<TaskDto> SomedayIn(int categoryId) =>
        _tasks.Values.Where(t => t.State == TaskStateDto.Someday && t.CategoryId == categoryId).OrderBy(t => t.SortOrder);

    public int SomedayCount(int categoryId) =>
        _tasks.Values.Count(t => t.State == TaskStateDto.Someday && t.CategoryId == categoryId);

    public IEnumerable<TaskDto> PrioritiesOn(DateOnly date) =>
        PlannedOn(date).Where(t => t.StartTime is null).OrderBy(t => t.SortOrder);

    public IEnumerable<TaskDto> ScheduledOn(DateOnly date) => PlannedOn(date).Where(t => t.StartTime is not null);

    public IEnumerable<WeeklyGoalDto> GoalsFor(int roleId) =>
        Goals.Where(g => g.RoleId == roleId).OrderBy(g => g.SortOrder);

    private IEnumerable<TaskDto> PlannedOn(DateOnly date) =>
        _tasks.Values.Where(t => (t.State is TaskStateDto.Planned or TaskStateDto.Done) && t.PlannedDate == date);

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
        WeekStart = RuDates.WeekStartOf(anyDate);
        NotifyChanged();
        try
        {
            ApplyWeek(await api.GetWeekAsync(WeekStart));
        }
        catch (ApiException ex)
        {
            LoadError = ex.Message;
        }

        NotifyChanged();
    }

    public Task NextWeekAsync() => GoToWeekAsync(WeekStart.AddDays(7));

    public Task PreviousWeekAsync() => GoToWeekAsync(WeekStart.AddDays(-7));

    public Task TodayAsync() => GoToWeekAsync(RuDates.Today);

    private void ApplyBoard(BoardDto board)
    {
        Settings = board.Settings;
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
        // Дела плана храним только для показанной недели
        foreach (var id in _tasks.Values.Where(t => t.State != TaskStateDto.Someday).Select(t => t.Id).ToList())
        {
            _tasks.Remove(id);
        }

        foreach (var task in week.Tasks)
        {
            _tasks[task.Id] = task;
        }

        Goals = [.. week.Goals];
    }

    private void NotifyChanged() => Changed?.Invoke();
}
