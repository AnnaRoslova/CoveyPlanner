using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;
using Planner.Core.Time;

namespace Planner.Core.Entities;

/// <summary>
/// Цель роли на неделю («большой камень»), которую нужно сделать <see cref="TimesPerWeek"/> раз.
/// Каждое выполнение — занятие: обычное дело с GoalId. Занятие появляется, когда цель перетаскивают
/// в день, или цель создаётся из дела ленты (тогда это дело — занятие без дня).
/// Цель выполнена, когда отмечено нужное число занятий.
/// </summary>
public class WeeklyGoal : ISortable
{
    public const int MinTimesPerWeek = 1;
    public const int MaxTimesPerWeek = 7;

    private readonly List<TaskItem> _sessions = [];

    private WeeklyGoal()
    {
    }

    public int Id { get; private set; }

    public int RoleId { get; private set; }

    /// <summary>Понедельник недели.</summary>
    public DateOnly WeekStart { get; private set; }

    public string Title { get; private set; } = "";

    /// <summary>Сколько раз за неделю (1–7).</summary>
    public int TimesPerWeek { get; private set; } = MinTimesPerWeek;

    public int SortOrder { get; set; }

    /// <summary>Все занятия цели, включая те, что в корзине. Репозиторий подгружает их вместе с целью.</summary>
    public IReadOnlyList<TaskItem> Sessions => _sessions;

    /// <summary>Занятия, которые считаются: без корзины.</summary>
    public IEnumerable<TaskItem> ActiveSessions => _sessions.Where(t => t.State != TaskState.Trashed);

    public int DoneCount => ActiveSessions.Count(t => t.State == TaskState.Done);

    public bool IsDone => DoneCount >= TimesPerWeek;

    /// <summary>Невыполненное занятие без дня (например, дело, из которого сделали цель) — первым встаёт в день.</summary>
    public TaskItem? UnscheduledSession =>
        ActiveSessions.FirstOrDefault(t => t.State == TaskState.Planned && t.PlannedDate is null);

    public static WeeklyGoal Create(int roleId, DateOnly weekStart, string title)
    {
        if (!Week.IsWeekStart(weekStart))
        {
            throw new DomainException("Неделя должна начинаться с понедельника.");
        }

        return new WeeklyGoal
        {
            RoleId = roleId,
            WeekStart = weekStart,
            Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название цели"),
        };
    }

    /// <summary>
    /// Переименовать цель. Занятия, названные как цель, переименовываются вместе с ней;
    /// занятия со своим названием («Бег 5 км») не трогаются.
    /// </summary>
    public void Rename(string title)
    {
        var oldTitle = Title;
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название цели");
        foreach (var session in _sessions.Where(t => t.Title == oldTitle))
        {
            session.Rename(Title);
        }
    }

    public void SetTimesPerWeek(int times)
    {
        if (times is < MinTimesPerWeek or > MaxTimesPerWeek)
        {
            throw new DomainException($"Сколько раз в неделю — от {MinTimesPerWeek} до {MaxTimesPerWeek}.");
        }

        TimesPerWeek = times;
    }

    /// <summary>Перенести к другой роли; занятия переходят вместе с целью.</summary>
    public void MoveToRole(int roleId)
    {
        RoleId = roleId;
        foreach (var session in _sessions)
        {
            session.RoleId = roleId;
        }
    }

    /// <summary>Сделать дело занятием цели. Через навигацию EF проставит GoalId и для ещё не сохранённой цели.</summary>
    public void AddSession(TaskItem task)
    {
        if (task.GoalId is not null && task.GoalId != Id)
        {
            throw new DomainException("Это дело уже связано с другой целью.");
        }

        task.RoleId = RoleId;
        if (!_sessions.Contains(task))
        {
            _sessions.Add(task);
        }
    }

    /// <summary>Отвязать занятие (дело остаётся как есть).</summary>
    public void RemoveSession(TaskItem task)
    {
        _sessions.Remove(task);
        task.DetachFromGoal();
    }
}
