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

    // Шаблон занятий: новые занятия берут эти значения (см. UpdateTemplate)

    /// <summary>Время занятий («07:30 Бег»), необязательно.</summary>
    public TimeOnly? StartTime { get; private set; }

    public Quadrant? Quadrant { get; private set; }

    public string? Notes { get; private set; }

    public int? DurationMinutes { get; private set; }

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
    /// Изменить шаблон занятий. Квадрант у занятий всегда от цели — меняется у всех.
    /// Название, время, заметки и длительность меняются у невыполненных занятий, если у занятия
    /// это поле не своё: «своё» — значит отличается от прежнего значения цели.
    /// </summary>
    /// <returns>Занятия, у которых убрали время: их нужно поставить в конец ручного списка дня.</returns>
    public IReadOnlyList<TaskItem> UpdateTemplate(
        string title, TimeOnly? startTime, Quadrant? quadrant, string? notes, int? durationMinutes)
    {
        var old = (Title, StartTime, Notes, DurationMinutes);

        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название цели");
        StartTime = startTime is { } t ? new TimeOnly(t.Hour, t.Minute) : null;
        Quadrant = quadrant;
        Notes = TextRules.Optional(notes, TextRules.NotesMaxLength, "Заметки");
        DurationMinutes = TextRules.Duration(durationMinutes);

        var lostTime = new List<TaskItem>();
        foreach (var session in _sessions)
        {
            session.Quadrant = Quadrant;
            if (session.State != TaskState.Planned)
            {
                continue; // выполненные — история, в корзине — не трогаем
            }

            if (session.Title == old.Title)
            {
                session.Rename(Title);
            }

            // Время бывает только у занятия в дне; занятие без дня получит время, когда встанет в день
            if (session.IsOnDay && session.StartTime == old.StartTime && session.StartTime != StartTime)
            {
                session.SetTime(StartTime);
                if (StartTime is null)
                {
                    lostTime.Add(session);
                }
            }

            if (session.Notes == old.Notes)
            {
                session.SetNotes(Notes);
            }

            if (session.DurationMinutes == old.DurationMinutes)
            {
                session.SetDuration(DurationMinutes);
            }
        }

        return lostTime;
    }

    /// <summary>Шаблон берётся из дела, из которого сделана цель (всё, кроме роли — она у цели своя).</summary>
    public void CopyTemplateFrom(TaskItem task) =>
        UpdateTemplate(task.Title, task.StartTime, task.Quadrant, task.Notes, task.DurationMinutes);

    /// <summary>Новое занятие получает значения цели. Время — когда занятие встанет в день.</summary>
    public void FillFromTemplate(TaskItem session)
    {
        session.Rename(Title);
        session.Quadrant = Quadrant;
        session.SetNotes(Notes);
        session.SetDuration(DurationMinutes);
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
            session.SetRoles([roleId]);
        }
    }

    /// <summary>Сделать дело занятием цели. Через навигацию EF проставит GoalId и для ещё не сохранённой цели.</summary>
    public void AddSession(TaskItem task)
    {
        if (task.GoalId is not null && task.GoalId != Id)
        {
            throw new DomainException("Это дело уже связано с другой целью.");
        }

        task.SetRoles([RoleId]); // у занятия ровно одна роль — роль цели
        task.Quadrant = Quadrant;
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
