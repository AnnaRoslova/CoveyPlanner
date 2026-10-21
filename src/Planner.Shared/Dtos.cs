using System.Text.Json.Serialization;

namespace Planner.Shared;

// Контракты API. Перечисления в JSON — строками.

[JsonConverter(typeof(JsonStringEnumConverter<TaskStateDto>))]
public enum TaskStateDto
{
    Someday,
    Planned,
    Done,
    Trashed,
}

[JsonConverter(typeof(JsonStringEnumConverter<SawDimensionDto>))]
public enum SawDimensionDto
{
    Body = 1,
    Mind = 2,
    Spirit = 3,
    Social = 4,
}

/// <summary>
/// Дело. RoleIds — роли дела (может быть несколько; у занятия цели — роли цели);
/// Quadrant — 1..4 или null; StartTime — необязательная метка времени;
/// DurationMinutes — необязательная примерная длительность. GoalId — цель, занятием которой является дело (null — не «камень»).
/// ParentId / ParentTitle — дело-проект, шагом которого является это (null — не шаг); StepOrder — место в списке шагов.
/// IsFromSomeday — дело хоть раз лежало в ленте (выполненное — в «Выполненном»).
/// </summary>
public sealed record TaskDto(
    int Id,
    string Title,
    string? Notes,
    int? CategoryId,
    IReadOnlyList<int> RoleIds,
    int? GoalId,
    int? Quadrant,
    TaskStateDto State,
    DateOnly? PlannedDate,
    TimeOnly? StartTime,
    int? DurationMinutes,
    int SortOrder,
    DateTime? TrashedAt,
    int? ParentId = null,
    string? ParentTitle = null,
    bool IsFromSomeday = false,
    int? StepOrder = null,
    bool IsProject = false,
    DateOnly? PlannedWeek = null,
    DateTime? UpdatedAt = null,
    DateTime? KeptAt = null);

public sealed record CategoryDto(int Id, string Name, string? Emoji, int SortOrder, bool IsExpanded);

public sealed record RoleDto(int Id, string Name, string Color, int SortOrder, SawDimensionDto? SawDimension)
{
    [JsonIgnore]
    public bool IsSharpenSaw => SawDimension is not null;
}

/// <summary>Занятие цели: дело-«камень». PlannedDate = null — занятие без дня.</summary>
public sealed record GoalSessionDto(int TaskId, DateOnly? PlannedDate, bool IsDone);

/// <summary>Роль цели и место цели в списке этой роли.</summary>
public sealed record GoalRoleDto(int RoleId, int SortOrder);

/// <summary>
/// Цель недели, которую нужно сделать TimesPerWeek раз, — шаблон занятий: новые занятия берут
/// StartTime, Quadrant, Notes, DurationMinutes. Sessions — её занятия (без корзины), по дням;
/// прогресс и «выполнено» считаются по ним. Roles — роли цели (хотя бы одна): цель видна под каждой, в каждой — на своём месте. IsRecurring — «каждую неделю»: цель появится и на следующей неделе;
/// AllowSeveralPerDay — можно несколько занятий в один день (иначе — не больше одного).
/// Days — дни, по которым занятия встают сами (битовая маска: Пн = 1, Вт = 2, … Вс = 64; 127 — каждый день); null — «сам раскладываю».
/// SeriesId — серия цели (та же цель на других неделях); IsHabit — «🫙 копить» серии: постоянная цель с ним — привычка с Id = SeriesId (см. HabitDto).
/// ProjectId / ProjectTitle — проект новых занятий; ProjectIsOpen = false — проект выполнен или в корзине (новые занятия — без проекта), ProjectIsTrashed — в корзине.
/// </summary>
public sealed record WeeklyGoalDto(
    int Id,
    IReadOnlyList<GoalRoleDto> Roles,
    DateOnly WeekStart,
    string Title,
    int TimesPerWeek,
    TimeOnly? StartTime,
    int? Quadrant,
    string? Notes,
    int? DurationMinutes,
    IReadOnlyList<GoalSessionDto> Sessions,
    bool IsRecurring = false,
    bool AllowSeveralPerDay = false,
    int? Days = null,
    int? SeriesId = null,
    bool IsHabit = false,
    int? ProjectId = null,
    string? ProjectTitle = null,
    bool ProjectIsOpen = true,
    bool ProjectIsTrashed = false)
{
    [JsonIgnore]
    public int DoneCount => Sessions.Count(s => s.IsDone);

    [JsonIgnore]
    public bool IsDone => DoneCount >= TimesPerWeek;

    /// <summary>Процент выполненных занятий недели (вниз до целого): 100 — только когда норма выполнена, сверх нормы — тоже 100.</summary>
    [JsonIgnore]
    public int Percent => TimesPerWeek <= 0 ? 0 : Math.Min(100, DoneCount * 100 / TimesPerWeek);

    [JsonIgnore]
    public IReadOnlyList<int> RoleIds => Roles.Select(r => r.RoleId).ToList();

    public bool HasRole(int roleId) => Roles.Any(r => r.RoleId == roleId);

    /// <summary>Место цели в списке роли (нет роли — в конце).</summary>
    public int SortOrderIn(int roleId) => Roles.FirstOrDefault(r => r.RoleId == roleId)?.SortOrder ?? int.MaxValue;
}

/// <summary>Всё, что не зависит от недели: роли, лента, палитра.</summary>
public sealed record BoardDto(
    IReadOnlyList<string> RolePalette,
    int RecommendedGoalsPerWeek,
    IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<TaskDto> SomedayTasks,
    int TrashCount,
    IReadOnlyList<TaskDto>? Steps = null,
    IReadOnlyList<TaskDto>? PlannedProjects = null,
    int StaleMonths = 3);

/// <summary>
/// Неделя: цели ролей и дела плана (с датой в пределах недели, а также занятия её целей без дня).
/// Steps — шаги проектов, видимых на неделе (для раскрытия «▸» и «☑ N/M»; без корзины).
/// </summary>
public sealed record WeekDto(
    DateOnly WeekStart, IReadOnlyList<WeeklyGoalDto> Goals, IReadOnlyList<TaskDto> Tasks, IReadOnlyList<TaskDto>? Steps = null);

/// <summary>
/// Цель после сохранения окна и её занятия в днях (у них могли поменяться поля по шаблону);
/// RemovedSessionIds — занятия, убранные в корзину при раскладке по новым дням (для «Вернуть»).
/// </summary>
public sealed record GoalUpdateResultDto(WeeklyGoalDto Goal, IReadOnlyList<TaskDto> Sessions, IReadOnlyList<int>? RemovedSessionIds = null);

/// <summary>Что уберёт выключение «каждую неделю» (fixes-2026-10.md, E1.2): уже созданные цели следующих недель и их невыполненные занятия.</summary>
public sealed record SeriesEndPreviewDto(int Goals, int OpenSessions);

/// <summary>Результат операции с целью: сама цель и занятие, которое поменялось.</summary>
public sealed record GoalResultDto(WeeklyGoalDto Goal, TaskDto? Task);

/// <summary>
/// После «сделать дело занятием цели»: дело, затронутые цели — прежняя и новая (с новым прогрессом) — и занятия,
/// которые дело заменило («одно занятие цели в день»; они в корзине — для «Вернуть»).
/// </summary>
public sealed record TaskGoalResultDto(TaskDto Task, IReadOnlyList<WeeklyGoalDto> Goals, IReadOnlyList<int> ReplacedTaskIds);

/// <summary>Итог недели или месяца привычки (см. Planner.Core.Habits.PeriodResult).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PeriodResultDto>))]
public enum PeriodResultDto
{
    BeforeStart,
    Done,
    Neutral,
    Paused,
    Missed,
    Vacation,
    Current,
}

/// <summary>Неделя привычки: понедельник, сколько скрепок, итог, норма той недели (null — цели не было).</summary>
public sealed record HabitWeekDto(DateOnly WeekStart, int Count, PeriodResultDto Result, int? Norm = null);

/// <summary>Месяц привычки (первое число) и итог.</summary>
public sealed record HabitMonthDto(DateOnly Month, PeriodResultDto Result);

/// <summary>
/// Посчитанное: всего скрепок, полные банки и сколько в текущей, эта неделя, 🔥 серия недель (WeekPaused — ⚠ пауза),
/// 🌙 серия месяцев, последние 26 недель и 7 месяцев (последние — текущие), в среднем за неделю, за месяц и год;
/// VacationUntil — сегодня каникулы до этого дня.
/// </summary>
public sealed record HabitStatsDto(
    int Total,
    int FullJars,
    int InJar,
    int ThisWeek,
    int WeekStreak,
    bool WeekPaused,
    int MonthStreak,
    bool MonthPaused,
    IReadOnlyList<HabitWeekDto> Weeks,
    IReadOnlyList<HabitMonthDto> Months,
    double AveragePerWeek,
    int ThisMonth,
    int ThisYear,
    DateOnly? VacationUntil);

/// <summary>
/// Привычка — постоянная серия целей с «🫙 копить» (Id — id серии) — и её статистика. Name — название цели;
/// Color — цвет первой роли; RoleIds — роли серии по порядку ролей: сначала измерения пилы, потом обычные (первая — группа на экране 🫙,
/// остальные — чипы); WeeklyNorm — норма этой недели («раз в неделю» цели); IsArchived — серия закончилась.
/// </summary>
public sealed record HabitDto(
    int Id,
    string Name,
    string Color,
    IReadOnlyList<int> RoleIds,
    int WeeklyNorm,
    int JarSize,
    int StartCount,
    bool IsArchived,
    HabitStatsDto Stats);

/// <summary>Каникулы с From по To включительно; HabitIds (id привычек) пусто — для всех привычек.</summary>
public sealed record VacationDto(int Id, DateOnly From, DateOnly To, IReadOnlyList<int> HabitIds);

/// <summary>Экран «Копилки»: все привычки (и в архиве) со статистикой на день Today и все каникулы.</summary>
public sealed record HabitsDto(DateOnly Today, IReadOnlyList<HabitDto> Habits, IReadOnlyList<VacationDto> Vacations);

/// <summary>После «+1 сегодня»: отмеченное занятие, его цель (новый прогресс) и привычка с новым счётом.</summary>
public sealed record PlusOneResultDto(TaskDto Task, WeeklyGoalDto Goal, HabitDto Habit);

/// <summary>Шаг дела в его окне: SortOrder — место в списке шагов; PlannedDate — день, если шаг стоит в дне; GoalId — шаг — занятие цели.</summary>
public sealed record StepDto(
    int Id, int ParentId, string Title, bool IsDone, DateTime? CompletedAt, int SortOrder, DateOnly? PlannedDate = null, int? GoalId = null);

/// <summary>Сводка проекта (дела с шагами): «☑ StepsDone/StepsTotal» — шаги без занятий целей; SessionsDone — выполненные занятия («12 занятий»).</summary>
public sealed record ProjectSummaryDto(int StepsDone, int StepsTotal, int SessionsDone);

/// <summary>Запись «Выполненного»: дело, день отметки, сводка проекта, «N недель» (null — выполненных шагов нет) и шаги (без корзины).</summary>
public sealed record DoneTaskDto(TaskDto Task, DateOnly Date, ProjectSummaryDto? Project, int? Weeks, IReadOnlyList<StepDto> Steps);

/// <summary>Сколько дел группы выполнено в году.</summary>
public sealed record DoneYearDto(int Year, int Count);

/// <summary>Группа «Выполненного» — категория ленты (CategoryId = null — «без категории»), счёт по годам и записи, свежие сверху.</summary>
public sealed record DoneGroupDto(int? CategoryId, string Name, string? Emoji, IReadOnlyList<DoneYearDto> Years, IReadOnlyList<DoneTaskDto> Tasks);

/// <summary>Дело после отметки; Parent — родитель, если отметился сам (все шаги сделаны); Session — занятие, отмеченное вместе с ним (R4).</summary>
public sealed record TaskDoneResultDto(TaskDto Task, TaskDto? Parent, TaskDto? Session = null);

/// <summary>«+ шаг» цели: цель (с проектом), новый шаг и проект, если его создали этим шагом (иначе null).</summary>
public sealed record GoalStepResultDto(WeeklyGoalDto Goal, TaskDto Step, TaskDto? CreatedProject);

/// <summary>Отчёт за период (N5): WeeksCount — недель, чей четверг в периоде; серии, разовые цели одной строкой, «Выполненное».</summary>
public sealed record ReportDto(
    DateOnly From, DateOnly To, int WeeksCount, IReadOnlyList<ReportSeriesDto> Series, ReportOneOffDto OneOff, IReadOnlyList<ReportDoneGroupDto> Done);

/// <summary>Привычка или постоянная цель за период: план (сумма норм недель), факт (скрепки), процент; 🔥 и ⚠ — на конец периода (у привычек).</summary>
public sealed record ReportSeriesDto(
    int Id, string Title, IReadOnlyList<string> Roles, bool IsHabit, int Plan, int Fact, int Percent, int? WeekStreak, bool Paused);

/// <summary>Разовые цели периода: «камней N, выполнено M».</summary>
public sealed record ReportOneOffDto(int Goals, int Done);

/// <summary>«Выполненное» за период — группа категории.</summary>
public sealed record ReportDoneGroupDto(int? CategoryId, string Name, string? Emoji, IReadOnlyList<ReportDoneTaskDto> Tasks);

/// <summary>Выполненное дело: день отметки и сколько шагов сделано.</summary>
public sealed record ReportDoneTaskDto(int Id, string Title, DateOnly Date, int StepsDone);

/// <summary>
/// Дашборд за период (batch-ux.md, BD8): недели периода (понедельники), привычки со скрепками по дням и сериями, каникулы,
/// занятия по измерениям пилы, выполненное по ролям, II квадрант по неделям, давно лежащие мысли.
/// </summary>
public sealed record DashboardDto(
    DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Weeks, IReadOnlyList<DashboardHabitDto> Habits,
    IReadOnlyList<DashboardVacationDto> Vacations, IReadOnlyList<DashboardRoleDto> Saw, IReadOnlyList<DashboardRoleDto> Roles,
    IReadOnlyList<DashboardQuadrantDto> Quadrants, DashboardStaleDto Stale);

/// <summary>Привычка на тепловой карте: скрепки периода (день повторяется по числу скрепок), 🔥 сейчас и лучшая серия недель.</summary>
public sealed record DashboardHabitDto(
    int Id, string Title, string Color, bool IsArchived, int WeekStreak, int BestWeekStreak, IReadOnlyList<DateOnly> Clips);

/// <summary>Каникулы, задевающие период; HabitIds пусто — у всех привычек.</summary>
public sealed record DashboardVacationDto(DateOnly From, DateOnly To, IReadOnlyList<int> HabitIds);

/// <summary>Число у роли; RoleId = null — «без роли».</summary>
public sealed record DashboardRoleDto(int? RoleId, string Name, string Color, bool IsSaw, int Count);

/// <summary>Неделя: выполненных с квадрантом II, с отмеченным квадрантом, без квадранта.</summary>
public sealed record DashboardQuadrantDto(DateOnly WeekStart, int Second, int Marked, int Unmarked);

/// <summary>«Давно лежат»: срок в месяцах, сколько мыслей старше, самые старые.</summary>
public sealed record DashboardStaleDto(int Months, int Count, IReadOnlyList<DashboardThoughtDto> Oldest);

/// <summary>Давняя мысль: с какого момента лежит без изменений (UpdatedAt, BS1).</summary>
public sealed record DashboardThoughtDto(int Id, string Title, DateTime Since);

/// <summary>Настройки на сервере (batch-ux.md, BS2): срок «давно лежат», месяцев.</summary>
public sealed record SettingsDto(int StaleMonths);

/// <summary>Плашка о мыслях, перешагнувших срок (BS4): срок и сами мысли (самые старые сверху); пусто — плашки нет.</summary>
public sealed record StaleNoticeDto(int Months, IReadOnlyList<TaskDto> Thoughts);
