using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Time;

namespace Planner.Core.Entities;

/// <summary>
/// Серия — «одна и та же цель» на разных неделях. Бывает двух видов:
/// <list type="bullet">
/// <item>постоянная («каждую неделю», <see cref="IsRecurring"/>): на каждую неделю от <see cref="FirstWeek"/>
/// до <see cref="LastWeek"/> (null — без конца) сама появляется цель по шаблону серии;</item>
/// <item>непостоянная: просто связывает цели, между которыми переносили занятия-«камни»,
/// чтобы следующий перенос на ту же неделю попал в уже созданную цель.</item>
/// </list>
/// Цели создаются лениво — когда неделю открывают (или переносят на неё занятие). <see cref="GeneratedThrough"/> —
/// до какой недели цели уже созданы: удалённая «только на этой неделе» цель поэтому сама не возвращается.
/// Постоянная серия с «🫙 копить» (<see cref="IsHabit"/>) — привычка: у неё банка со скрепками (<see cref="Services.HabitService"/>),
/// одна история на все правки цели; закончилась серия — привычка в архиве.
/// </summary>
public class GoalSeries
{
    private readonly List<SeriesRole> _roles = [];

    private GoalSeries()
    {
    }

    public int Id { get; private set; }

    /// <summary>Роли целей серии (часть шаблона).</summary>
    public IReadOnlyCollection<SeriesRole> Roles => _roles;

    public IReadOnlyList<int> RoleIds => _roles.Select(r => r.RoleId).Order().ToList();

    // Шаблон целей серии (как у WeeklyGoal)

    public string Title { get; private set; } = "";

    public int TimesPerWeek { get; private set; } = WeeklyGoal.MinTimesPerWeek;

    public TimeOnly? StartTime { get; private set; }

    public Quadrant? Quadrant { get; private set; }

    public string? Notes { get; private set; }

    public int? DurationMinutes { get; private set; }

    /// <summary>Можно ли несколько занятий в один день.</summary>
    public bool AllowSeveralPerDay { get; private set; }

    /// <summary>Дни, по которым занятия встают сами; null — «сам раскладываю».</summary>
    public Weekdays? Days { get; private set; }

    /// <summary>«Каждую неделю»: цели на следующие недели появляются сами.</summary>
    public bool IsRecurring { get; private set; }

    /// <summary>Понедельник первой недели серии.</summary>
    public DateOnly FirstWeek { get; private set; }

    /// <summary>Понедельник последней недели постоянной серии; null — без конца.</summary>
    public DateOnly? LastWeek { get; private set; }

    /// <summary>До этой недели (включительно) цели серии уже созданы.</summary>
    public DateOnly GeneratedThrough { get; private set; }

    /// <summary>
    /// Проект новых занятий (часть шаблона): занятия, которые цели серии создают сами, становятся шагами этого дела. null — без проекта.
    /// </summary>
    public int? ProjectId { get; private set; }

    /// <summary>
    /// «🫙 копить»: постоянная серия с этим флагом — привычка (банка, серии недель и месяцев). Свойство серии целиком:
    /// включили — считается вся её история. У новой постоянной цели пилы включено по умолчанию, у обычных ролей — выключено.
    /// </summary>
    public bool IsHabit { get; private set; }

    // Банка привычки: действует, только если серия — привычка (постоянная, «🫙 копить»)

    public const int DefaultJarSize = 30;
    public const int MinJarSize = 1;
    public const int MaxJarSize = 1000;
    public const int MaxStartCount = 1_000_000;

    /// <summary>Сколько скрепок вмещает банка.</summary>
    public int JarSize { get; private set; } = DefaultJarSize;

    /// <summary>Стартовое число: скрепки, накопленные до привычки (их дат нет — в серии недель не входят).</summary>
    public int StartCount { get; private set; }

    /// <summary>Серия закончилась (сняли «каждую неделю» или удалили «с этой недели и дальше») — привычка в архиве.</summary>
    public bool IsEnded => LastWeek is not null;

    /// <summary>Банка настроена (не как у новой) — такую законченную привычку видно в архиве, даже без скрепок.</summary>
    public bool HasCustomJar => JarSize != DefaultJarSize || StartCount != 0;

    /// <summary>Настройки банки привычки.</summary>
    public void SetJar(int startCount, int jarSize)
    {
        JarSize = jarSize is >= MinJarSize and <= MaxJarSize
            ? jarSize
            : throw new DomainException($"В банке — от {MinJarSize} до {MaxJarSize} скрепок.");
        StartCount = startCount is >= 0 and <= MaxStartCount
            ? startCount
            : throw new DomainException($"Стартовое число — от 0 до {MaxStartCount}.");
    }

    /// <summary>Включить или выключить «🫙 копить». Выключили — банка и каникулы остаются: включили снова — всё как было.</summary>
    public void SetHabit(bool isHabit) => IsHabit = isHabit;

    /// <summary>
    /// Удалить привычку (из архива): «копить» выключается, банка — как у новой; серия, цели и дела остаются.
    /// Снова включили «копить» — история (выполненные занятия) на месте.
    /// </summary>
    public void DeleteHabit()
    {
        JarSize = DefaultJarSize;
        StartCount = 0;
        IsHabit = false;
    }

    /// <summary>Серия из цели: шаблон — её, первая неделя — её неделя.</summary>
    public static GoalSeries StartFrom(WeeklyGoal goal, bool recurring)
    {
        var series = new GoalSeries
        {
            FirstWeek = goal.WeekStart,
            GeneratedThrough = goal.WeekStart,
            IsRecurring = recurring,
        };
        series.CopyTemplateFrom(goal);
        return series;
    }

    /// <summary>
    /// Повторится ли цель серии после недели <paramref name="week"/> — так в окне цели показывается «каждую неделю».
    /// </summary>
    public bool RepeatsAfter(DateOnly week) => IsRecurring && (LastWeek is null || LastWeek > week);

    /// <summary>Шаблон серии — из цели (её поменяли в окне или поменяли её роли).</summary>
    public void CopyTemplateFrom(WeeklyGoal goal)
    {
        var roleIds = goal.RoleIds;
        _roles.RemoveAll(r => !roleIds.Contains(r.RoleId));
        _roles.AddRange(roleIds.Where(id => _roles.All(r => r.RoleId != id)).Select(id => new SeriesRole(id)));
        Title = goal.Title;
        TimesPerWeek = goal.TimesPerWeek;
        StartTime = goal.StartTime;
        Quadrant = goal.Quadrant;
        Notes = goal.Notes;
        DurationMinutes = goal.DurationMinutes;
        AllowSeveralPerDay = goal.AllowSeveralPerDay;
        Days = goal.Days;
        ProjectId = goal.ProjectId;
    }

    /// <summary>
    /// Недели, на которые постоянной серии пора создать цели, — до <paramref name="upTo"/> включительно.
    /// Сразу отмечает их созданными (<see cref="GeneratedThrough"/>).
    /// </summary>
    public IReadOnlyList<DateOnly> TakeWeeksToGenerate(DateOnly upTo)
    {
        if (!IsRecurring)
        {
            return [];
        }

        var last = LastWeek is { } end && end < upTo ? end : upTo;
        var weeks = new List<DateOnly>();
        for (var week = GeneratedThrough.AddDays(Week.DaysInWeek); week <= last; week = week.AddDays(Week.DaysInWeek))
        {
            if (week >= FirstWeek)
            {
                weeks.Add(week);
            }
        }

        if (last > GeneratedThrough)
        {
            GeneratedThrough = last;
        }

        return weeks;
    }

    /// <summary>Убрать роль из шаблона (роль удалили). false — ролей не осталось: серию нужно удалить.</summary>
    public bool RemoveRole(int roleId)
    {
        _roles.RemoveAll(r => r.RoleId == roleId);
        return _roles.Count > 0;
    }

    /// <summary>
    /// Серия заканчивается на неделе <paramref name="week"/> (её цель остаётся, следующих не будет). Уже созданные цели после неё
    /// удаляет вызывающий — значит, они больше не созданы: <see cref="GeneratedThrough"/> откатывается на эту неделю,
    /// и снова включённая серия создаст их заново (fixes-2026-10.md, E1.1).
    /// </summary>
    public void StopAfter(DateOnly week)
    {
        EnsureWeekStart(week);
        LastWeek = week;
        if (GeneratedThrough > week)
        {
            GeneratedThrough = week;
        }
    }

    /// <summary>
    /// Снова «каждую неделю» с недели <paramref name="week"/>. Недели между прежним концом и этой — не заполняются.
    /// «Копить» здесь не меняется — его ставит окно цели (см. GoalService).
    /// </summary>
    public void RecurFrom(DateOnly week)
    {
        EnsureWeekStart(week);
        IsRecurring = true;
        LastWeek = null;
        if (GeneratedThrough < week)
        {
            GeneratedThrough = week;
        }
    }

    private static void EnsureWeekStart(DateOnly week)
    {
        if (!Week.IsWeekStart(week))
        {
            throw new DomainException("Неделя должна начинаться с понедельника.");
        }
    }
}
