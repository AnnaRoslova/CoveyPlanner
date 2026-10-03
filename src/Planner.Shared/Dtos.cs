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
/// Дело. RoleIds — роли дела (может быть несколько, у занятия цели — одна, роль цели);
/// Quadrant — 1..4 или null; StartTime — необязательная метка времени;
/// DurationMinutes — необязательная примерная длительность. GoalIds — цели, в которые засчитывается дело (первая — главная; пусто — не «камень»).
/// </summary>
public sealed record TaskDto(
    int Id,
    string Title,
    string? Notes,
    int? CategoryId,
    IReadOnlyList<int> RoleIds,
    IReadOnlyList<int> GoalIds,
    int? Quadrant,
    TaskStateDto State,
    DateOnly? PlannedDate,
    TimeOnly? StartTime,
    int? DurationMinutes,
    int SortOrder,
    DateTime? TrashedAt)
{
    /// <summary>Главная цель дела (от неё шаблон и квадрант); null — дело не засчитывается ни в одну цель.</summary>
    [JsonIgnore]
    public int? PrimaryGoalId => GoalIds.Count > 0 ? GoalIds[0] : null;
}

public sealed record CategoryDto(int Id, string Name, string? Emoji, int SortOrder, bool IsExpanded);

public sealed record RoleDto(int Id, string Name, string Color, int SortOrder, SawDimensionDto? SawDimension)
{
    [JsonIgnore]
    public bool IsSharpenSaw => SawDimension is not null;
}

/// <summary>Занятие цели: дело-«камень». PlannedDate = null — занятие без дня.</summary>
public sealed record GoalSessionDto(int TaskId, DateOnly? PlannedDate, bool IsDone);

/// <summary>
/// Цель недели, которую нужно сделать TimesPerWeek раз, — шаблон занятий: новые занятия берут
/// StartTime, Quadrant, Notes, DurationMinutes. Sessions — её занятия (без корзины), по дням;
/// прогресс и «выполнено» считаются по ним. IsRecurring — «каждую неделю»: цель появится и на следующей неделе;
/// AllowSeveralPerDay — можно несколько занятий в один день (иначе — не больше одного).
/// Days — дни, по которым занятия встают сами (битовая маска: Пн = 1, Вт = 2, … Вс = 64; 127 — каждый день); null — «сам раскладываю».
/// </summary>
public sealed record WeeklyGoalDto(
    int Id,
    int RoleId,
    DateOnly WeekStart,
    string Title,
    int TimesPerWeek,
    TimeOnly? StartTime,
    int? Quadrant,
    string? Notes,
    int? DurationMinutes,
    int SortOrder,
    IReadOnlyList<GoalSessionDto> Sessions,
    bool IsRecurring = false,
    bool AllowSeveralPerDay = false,
    int? Days = null)
{
    [JsonIgnore]
    public int DoneCount => Sessions.Count(s => s.IsDone);

    [JsonIgnore]
    public bool IsDone => DoneCount >= TimesPerWeek;
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
/// После «засчитать дело в цели»: дело, затронутые цели (с новым прогрессом) и занятия, которые дело заменило
/// («одно занятие цели в день»; они в корзине — для «Вернуть»).
/// </summary>
public sealed record TaskGoalsResultDto(TaskDto Task, IReadOnlyList<WeeklyGoalDto> Goals, IReadOnlyList<int> ReplacedTaskIds);
