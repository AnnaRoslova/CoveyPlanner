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

/// <summary>
/// StartTime / DurationMinutes = null — без времени / длительности. RoleIds — роли дела (null или пусто — без ролей).
/// У занятия цели роль и Quadrant берутся от цели. HabitId — копилка: null — не менять, 0 — без копилки.
/// </summary>
public sealed record UpdateTaskRequest(
    string Title, string? Notes, IReadOnlyList<int>? RoleIds, int? Quadrant, TimeOnly? StartTime, int? DurationMinutes,
    int? HabitId = null);

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

/// <summary>IsRecurring — «каждую неделю»; null — как принято у роли (у измерений пилы — да, у обычных ролей — нет).</summary>
public sealed record CreateGoalRequest(int RoleId, DateOnly WeekStart, string Title, bool? IsRecurring = null);

/// <summary>Окно цели целиком. null у необязательных полей — «не задано»; IsRecurring / AllowSeveralPerDay = null — «каждую неделю» / «можно несколько раз в день» не менять.
/// Days — дни (маска, как в WeeklyGoalDto): null — не менять, 0 — «сам раскладываю». RoleIds — роли цели (хотя бы одна).
/// HabitId — копилка: null — не менять, 0 — без копилки.</summary>
public sealed record UpdateGoalRequest(
    string Title,
    IReadOnlyList<int> RoleIds,
    int TimesPerWeek,
    TimeOnly? StartTime,
    int? Quadrant,
    string? Notes,
    int? DurationMinutes,
    bool? IsRecurring = null,
    bool? AllowSeveralPerDay = null,
    int? Days = null,
    int? HabitId = null);

/// <summary>Клик по отметке цели. Today — сегодняшняя дата у пользователя.</summary>
public sealed record ToggleGoalRequest(DateOnly Today);

/// <summary>
/// Цель перетащили в список роли RoleId на позицию Index. FromRoleId — из-под какой роли тащили;
/// AddRole = false — перенос (роль-источник заменяется), true (с Shift) — роль добавляется к остальным.
/// </summary>
public sealed record MoveGoalRequest(int RoleId, int Index, int? FromRoleId = null, bool AddRole = false);


public sealed record GoalFromTaskRequest(int TaskId, int RoleId, DateOnly WeekStart, int Index);

/// <summary>CategoryId = null — в «неразобранное».</summary>
public sealed record GoalToSomedayRequest(int? CategoryId, int Index);

/// <summary>Занятием какой цели недели сделать дело (null — отвязать от цели).</summary>
public sealed record SetTaskGoalRequest(int? GoalId);

/// <summary>
/// Окно копилки (создать или изменить). Color = null — следующий цвет палитры; IsArchived = null — не менять.
/// Kind — привычка или коллекция; YearGoal — цель коллекции на год (null — без цели); CategoryIds — категории ленты коллекции (null — не менять).
/// </summary>
public sealed record SaveHabitRequest(
    string Name,
    string? Emoji,
    string? Color,
    int WeeklyNorm,
    int JarSize = 30,
    int StartCount = 0,
    bool? IsArchived = null,
    HabitKindDto Kind = HabitKindDto.Habit,
    int? YearGoal = null,
    IReadOnlyList<int>? CategoryIds = null);

/// <summary>«+1 сегодня». Today — сегодняшняя дата у пользователя.</summary>
public sealed record PlusOneRequest(DateOnly Today);

/// <summary>Каникулы с From по To включительно; HabitIds пусто или null — для всех копилок.</summary>
public sealed record CreateVacationRequest(DateOnly From, DateOnly To, IReadOnlyList<int>? HabitIds);

/// <summary>Новый шаг (в конец) или новое название шага.</summary>
public sealed record StepRequest(string Title);
