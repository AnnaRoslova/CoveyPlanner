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
    DateTime? TrashedAt);

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
    bool IsHabit = false)
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
    int TrashCount);

/// <summary>Неделя: цели ролей и дела плана (с датой в пределах недели, а также занятия её целей без дня).</summary>
public sealed record WeekDto(DateOnly WeekStart, IReadOnlyList<WeeklyGoalDto> Goals, IReadOnlyList<TaskDto> Tasks);

/// <summary>
/// Цель после сохранения окна и её занятия в днях (у них могли поменяться поля по шаблону);
/// RemovedSessionIds — занятия, убранные в корзину при раскладке по новым дням (для «Вернуть»).
/// </summary>
public sealed record GoalUpdateResultDto(WeeklyGoalDto Goal, IReadOnlyList<TaskDto> Sessions, IReadOnlyList<int>? RemovedSessionIds = null);

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

/// <summary>Шаг дела (дело-чеклист): видно только в окне его дела.</summary>
public sealed record StepDto(int Id, int ParentId, string Title, bool IsDone, DateTime? CompletedAt, int SortOrder);
