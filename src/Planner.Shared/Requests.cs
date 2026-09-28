using System.Text.Json.Serialization;

namespace Planner.Shared;

/// <summary>Куда положить дело. В JSON различается полем "kind".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SomedayTargetDto), "someday")]
[JsonDerivedType(typeof(DayPrioritiesTargetDto), "priorities")]
[JsonDerivedType(typeof(GridTargetDto), "grid")]
public abstract record TaskTargetDto;

/// <summary>В ленту: категория и позиция (0 — наверх).</summary>
public sealed record SomedayTargetDto(int CategoryId, int Index) : TaskTargetDto;

/// <summary>В «Приоритеты дня»: позиция считается без самого перетаскиваемого дела.</summary>
public sealed record DayPrioritiesTargetDto(DateOnly Date, int Index) : TaskTargetDto;

/// <summary>В сетку часов (сервер выровняет по шагу сетки).</summary>
public sealed record GridTargetDto(DateOnly Date, TimeOnly Start, int DurationMinutes) : TaskTargetDto;

public sealed record CreateTaskRequest(string Title, TaskTargetDto Target);

public sealed record UpdateTaskRequest(string Title, string? Notes, int? RoleId, int? Quadrant);

public sealed record SetDoneRequest(bool IsDone);

public sealed record ReorderRequest(IReadOnlyList<int> Ids);

public sealed record CreateCategoryRequest(string Name);

/// <summary>null — поле не менять.</summary>
public sealed record UpdateCategoryRequest(string? Name, bool? IsExpanded);

public sealed record CreateRoleRequest(string Name, string? Color);

/// <summary>null — поле не менять.</summary>
public sealed record UpdateRoleRequest(string? Name, string? Color);

public sealed record CreateGoalRequest(int RoleId, DateOnly WeekStart, string Title);

/// <summary>null — поле не менять.</summary>
public sealed record UpdateGoalRequest(string? Title, bool? IsDone);

public sealed record MoveGoalRequest(int RoleId, int Index);

public sealed record GoalFromTaskRequest(int TaskId, int RoleId, DateOnly WeekStart, int Index);

public sealed record GoalToSomedayRequest(int CategoryId, int Index);
