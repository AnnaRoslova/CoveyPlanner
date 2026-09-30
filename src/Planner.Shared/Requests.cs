using System.Text.Json.Serialization;

namespace Planner.Shared;

/// <summary>Куда положить дело. В JSON различается полем "kind".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SomedayTargetDto), "someday")]
[JsonDerivedType(typeof(DayTargetDto), "day")]
public abstract record TaskTargetDto;

/// <summary>В ленту: категория и позиция (0 — наверх).</summary>
public sealed record SomedayTargetDto(int CategoryId, int Index) : TaskTargetDto;

/// <summary>
/// В день. Index — позиция среди дел дня БЕЗ времени, считая без самого перетаскиваемого дела.
/// Дела со временем всегда стоят первыми, по времени.
/// </summary>
public sealed record DayTargetDto(DateOnly Date, int Index) : TaskTargetDto;

public sealed record CreateTaskRequest(string Title, TaskTargetDto Target);

/// <summary>StartTime = null — без времени.</summary>
public sealed record UpdateTaskRequest(string Title, string? Notes, int? RoleId, int? Quadrant, TimeOnly? StartTime);

public sealed record SetDoneRequest(bool IsDone);

public sealed record ReorderRequest(IReadOnlyList<int> Ids);

public sealed record CreateCategoryRequest(string Name, string? Emoji);

/// <summary>null — поле не менять; Emoji = "" — убрать иконку.</summary>
public sealed record UpdateCategoryRequest(string? Name, string? Emoji, bool? IsExpanded);

public sealed record CreateRoleRequest(string Name, string? Color);

/// <summary>null — поле не менять.</summary>
public sealed record UpdateRoleRequest(string? Name, string? Color);

public sealed record CreateGoalRequest(int RoleId, DateOnly WeekStart, string Title);

/// <summary>null — поле не менять.</summary>
public sealed record UpdateGoalRequest(string? Title, int? TimesPerWeek);

/// <summary>Клик по отметке цели. Today — сегодняшняя дата у пользователя.</summary>
public sealed record ToggleGoalRequest(DateOnly Today);

public sealed record MoveGoalRequest(int RoleId, int Index);

public sealed record GoalFromTaskRequest(int TaskId, int RoleId, DateOnly WeekStart, int Index);

public sealed record GoalToSomedayRequest(int CategoryId, int Index);
