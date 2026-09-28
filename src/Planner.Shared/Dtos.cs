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

/// <summary>Дело. Quadrant — 1..4 или null; StartTime — необязательная метка времени.</summary>
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
    int SortOrder,
    DateTime? TrashedAt);

public sealed record CategoryDto(int Id, string Name, string? Emoji, int SortOrder, bool IsExpanded);

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

/// <summary>Всё, что не зависит от недели: роли, лента, палитра.</summary>
public sealed record BoardDto(
    IReadOnlyList<string> RolePalette,
    int RecommendedGoalsPerWeek,
    IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<TaskDto> SomedayTasks,
    int TrashCount,
    int? LastUsedCategoryId);

/// <summary>Неделя: цели ролей и дела плана (с датой в пределах недели).</summary>
public sealed record WeekDto(DateOnly WeekStart, IReadOnlyList<WeeklyGoalDto> Goals, IReadOnlyList<TaskDto> Tasks);

/// <summary>Результат операции с целью: сама цель и связанное дело, если оно поменялось.</summary>
public sealed record GoalResultDto(WeeklyGoalDto Goal, TaskDto? Task);

/// <summary>Коды ошибок в ProblemDetails (поле "code"), на которые клиент реагирует особо.</summary>
public static class ProblemCodes
{
    /// <summary>Дело возвращается в ленту без категории — спросить категорию и повторить запрос с ?categoryId=.</summary>
    public const string CategoryRequired = "category-required";
}
