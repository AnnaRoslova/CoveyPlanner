using System.Text.Json.Serialization;

namespace Planner.Shared;

/// <summary>Куда положить дело. В JSON различается полем "kind".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SomedayTargetDto), "someday")]
[JsonDerivedType(typeof(DayTargetDto), "day")]
public abstract record TaskTargetDto;

/// <summary>В ленту: категория (null — «неразобранное») и позиция (0 — наверх).</summary>
public sealed record SomedayTargetDto(int? CategoryId, int Index) : TaskTargetDto;

/// <summary>
/// В день. Index — позиция среди дел дня БЕЗ времени, считая без самого перетаскиваемого дела.
/// Дела со временем всегда стоят первыми, по времени.
/// </summary>
public sealed record DayTargetDto(DateOnly Date, int Index) : TaskTargetDto;

public sealed record CreateTaskRequest(string Title, TaskTargetDto Target);

/// <summary>StartTime / DurationMinutes = null — без времени / длительности. У занятия цели RoleId и Quadrant берутся от цели.</summary>
public sealed record UpdateTaskRequest(
    string Title, string? Notes, int? RoleId, int? Quadrant, TimeOnly? StartTime, int? DurationMinutes);

public sealed record SetDoneRequest(bool IsDone);

public sealed record ReorderRequest(IReadOnlyList<int> Ids);

/// <summary>AtTop — новая категория встаёт в начало списка (кнопка «+ категория» сверху ленты), иначе — в конец.</summary>
public sealed record CreateCategoryRequest(string Name, string? Emoji, bool AtTop = false);

/// <summary>Раскрыть или свернуть все категории разом.</summary>
public sealed record SetAllExpandedRequest(bool IsExpanded);

/// <summary>null — поле не менять; Emoji = "" — убрать иконку.</summary>
public sealed record UpdateCategoryRequest(string? Name, string? Emoji, bool? IsExpanded);

public sealed record CreateRoleRequest(string Name, string? Color);

/// <summary>null — поле не менять.</summary>
public sealed record UpdateRoleRequest(string? Name, string? Color);

public sealed record CreateGoalRequest(int RoleId, DateOnly WeekStart, string Title);

/// <summary>Окно цели целиком. null у необязательных полей — «не задано».</summary>
public sealed record UpdateGoalRequest(
    string Title,
    int RoleId,
    int TimesPerWeek,
    TimeOnly? StartTime,
    int? Quadrant,
    string? Notes,
    int? DurationMinutes);

/// <summary>Клик по отметке цели. Today — сегодняшняя дата у пользователя.</summary>
public sealed record ToggleGoalRequest(DateOnly Today);

public sealed record MoveGoalRequest(int RoleId, int Index);

public sealed record GoalFromTaskRequest(int TaskId, int RoleId, DateOnly WeekStart, int Index);

/// <summary>CategoryId = null — в «неразобранное».</summary>
public sealed record GoalToSomedayRequest(int? CategoryId, int Index);
