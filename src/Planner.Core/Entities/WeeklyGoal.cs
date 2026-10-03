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

    private readonly List<TaskGoal> _links = [];

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

    /// <summary>Можно ли ставить несколько занятий в один день («Чтение» утром и вечером). По умолчанию — нет.</summary>
    public bool AllowSeveralPerDay { get; private set; }

    /// <summary>
    /// Дни, по которым занятия встают сами (у постоянной цели — на каждой новой неделе). null — «сам раскладываю».
    /// Если дни выбраны, «раз в неделю» равно их числу.
    /// </summary>
    public Weekdays? Days { get; private set; }

    public int SortOrder { get; set; }

    /// <summary>Серия: та же цель на других неделях (постоянная — «каждую неделю»). null — разовая цель.</summary>
    public int? SeriesId { get; private set; }

    public GoalSeries? Series { get; private set; }

    /// <summary>Повторяется ли цель на следующей неделе («каждую неделю» в окне цели).</summary>
    public bool RepeatsAfterThisWeek => Series?.RepeatsAfter(WeekStart) ?? false;

    /// <summary>Все занятия цели, включая те, что в корзине. Репозиторий подгружает их вместе с целью.</summary>
    public IReadOnlyList<TaskItem> Sessions => _links.Select(l => l.Task).ToList();

    /// <summary>Связи с делами-занятиями (таблица TaskGoals).</summary>
    public IReadOnlyCollection<TaskGoal> Links => _links;

    /// <summary>Занятия, которые считаются: без корзины.</summary>
    public IEnumerable<TaskItem> ActiveSessions => Sessions.Where(t => t.State != TaskState.Trashed);

    public int DoneCount => ActiveSessions.Count(t => t.State == TaskState.Done);

    public bool IsDone => DoneCount >= TimesPerWeek;

    /// <summary>Есть ли у цели занятие (выполненное или нет, кроме корзины) в этот день, не считая <paramref name="except"/>.</summary>
    public bool HasSessionOn(DateOnly date, TaskItem? except = null) =>
        ActiveSessions.Any(t => t.PlannedDate == date && t != except);

    /// <summary>Поставить занятие в день можно, если там ещё нет занятия этой цели (или разрешено несколько в день).</summary>
    public void EnsureDayIsFree(DateOnly date, TaskItem? except = null)
    {
        if (!AllowSeveralPerDay && HasSessionOn(date, except))
        {
            throw new DomainException(DayTakenMessage);
        }
    }

    public const string DayTakenMessage = "Уже есть в этот день.";

    public void SetAllowSeveralPerDay(bool allow) => AllowSeveralPerDay = allow;

    /// <summary>Выбрать дни (None или null — «сам раскладываю»). Выбраны — «раз в неделю» = их число.</summary>
    public void SetDays(Weekdays? days)
    {
        var chosen = days & Weekdays.All;
        Days = chosen is null or Weekdays.None ? null : chosen;
        if (Days is { } set)
        {
            TimesPerWeek = set.Count();
        }
    }

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

    /// <summary>Цель постоянной серии на неделю: всё — из шаблона серии, занятий нет.</summary>
    public static WeeklyGoal FromSeries(GoalSeries series, DateOnly weekStart)
    {
        var goal = Create(series.RoleId, weekStart, series.Title);
        goal.TimesPerWeek = series.TimesPerWeek;
        goal.StartTime = series.StartTime;
        goal.Quadrant = series.Quadrant;
        goal.Notes = series.Notes;
        goal.DurationMinutes = series.DurationMinutes;
        goal.AllowSeveralPerDay = series.AllowSeveralPerDay;
        goal.Days = series.Days;
        goal.Series = series;
        return goal;
    }

    /// <summary>
    /// Та же цель на другой неделе — для занятия, которое туда перенесли: шаблон этой цели, но 1 раз в неделю
    /// и без повтора; цель входит в ту же серию.
    /// </summary>
    public WeeklyGoal CopyForWeek(DateOnly weekStart)
    {
        var goal = Create(RoleId, weekStart, Title);
        goal.StartTime = StartTime;
        goal.Quadrant = Quadrant;
        goal.Notes = Notes;
        goal.DurationMinutes = DurationMinutes;
        goal.AllowSeveralPerDay = AllowSeveralPerDay;
        goal.Series = Series;
        return goal;
    }

    /// <summary>Войти в серию (только что созданную из этой цели).</summary>
    public void JoinSeries(GoalSeries series) => Series = series;

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
        foreach (var session in Sessions.Where(IsPrimaryFor))
        {
            // Квадрант — от главной цели, а если у неё нет — от следующей связанной (так же при связывании)
            if (!session.RefreshFromGoals())
            {
                session.Quadrant = Quadrant;
            }

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

        // Дни выбраны — «раз в неделю» задают они
        TimesPerWeek = Days is { } days ? days.Count() : times;
    }

    /// <summary>
    /// Перенести к другой роли; занятия переходят вместе с целью (роли занятия — роли всех его целей;
    /// у дела нескольких целей их должен подгрузить сервис — иначе роль просто заменяется).
    /// </summary>
    public void MoveToRole(int roleId)
    {
        var oldRoleId = RoleId;
        RoleId = roleId;
        foreach (var session in Sessions)
        {
            if (!session.RefreshFromGoals())
            {
                session.SetRoles(session.RoleIds.Where(id => id != oldRoleId).Append(roleId));
            }
        }
    }

    /// <summary>Эта цель — главная у дела (от неё шаблон и квадрант).</summary>
    public bool IsPrimaryFor(TaskItem task) =>
        task.PrimaryGoalLink is { } primary && (primary.Goal == this || (Id != 0 && primary.GoalId == Id));

    /// <summary>
    /// Засчитать дело в эту цель (связь TaskGoals; через навигацию EF проставит Id и для ещё не сохранённой цели).
    /// Первая связь дела — главная: роль и квадрант от этой цели; следующая — роли всех целей.
    /// </summary>
    public void AddSession(TaskItem task)
    {
        if (_links.Any(l => l.Task == task))
        {
            return;
        }

        var link = new TaskGoal(task, this, task.NextGoalPosition);
        _links.Add(link);
        task.AddGoalLink(link);
        if (task.GoalLinks.Count == 1)
        {
            task.SetRoles([RoleId]);
            task.Quadrant = Quadrant;
        }
        else if (!task.RefreshFromGoals())
        {
            task.SetRoles(task.RoleIds.Append(RoleId));
        }
    }

    /// <summary>Перестать засчитывать дело в эту цель (дело остаётся; роли — от оставшихся целей, если они загружены).</summary>
    public void RemoveSession(TaskItem task)
    {
        if (_links.FirstOrDefault(l => l.Task == task) is not { } link)
        {
            return;
        }

        _links.Remove(link);
        task.RemoveGoalLink(link);
        task.RefreshFromGoals();
    }
}
