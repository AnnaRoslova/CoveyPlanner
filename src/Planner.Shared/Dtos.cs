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

/// <summary>Дело. Quadrant — 1..4 или null.</summary>
public sealed record TaskDto(
    int Id,
    string Title,
    string? Notes,
    int? CategoryId,
    int? RoleId,
    int? Quadrant,
    TaskStateDto State,
    DateOnly? PlannedDate,
    TimeOnly? StartTime,
    int? DurationMinutes,
    int SortOrder,
    DateTime? TrashedAt);

public sealed record CategoryDto(int Id, string Name, int SortOrder, bool IsExpanded, bool IsSystem);

public sealed record RoleDto(int Id, string Name, string Color, int SortOrder, SawDimensionDto? SawDimension)
{
    [JsonIgnore]
    public bool IsSharpenSaw => SawDimension is not null;
}

/// <summary>Цель недели. PlannedDate/StartTime — где сейчас связанное дело (если есть).</summary>
public sealed record WeeklyGoalDto(
    int Id,
    int RoleId,
    DateOnly WeekStart,
    string Title,
    bool IsDone,
    int SortOrder,
    int? TaskId,
    DateOnly? PlannedDate,
    TimeOnly? StartTime);

public sealed record SettingsDto(int DayStartHour, int DayEndHour, int SlotMinutes);

/// <summary>Всё, что не зависит от недели: настройки, роли, лента, палитра.</summary>
public sealed record BoardDto(
    SettingsDto Settings,
    IReadOnlyList<string> RolePalette,
    int RecommendedGoalsPerWeek,
    IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<TaskDto> SomedayTasks,
    int TrashCount);

/// <summary>Неделя: цели ролей и дела плана (с датой в пределах недели).</summary>
public sealed record WeekDto(DateOnly WeekStart, IReadOnlyList<WeeklyGoalDto> Goals, IReadOnlyList<TaskDto> Tasks);

/// <summary>Результат операции с целью: сама цель и связанное дело, если оно поменялось.</summary>
public sealed record GoalResultDto(WeeklyGoalDto Goal, TaskDto? Task);
