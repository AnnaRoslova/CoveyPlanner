using System.Text.Json.Serialization;

namespace Planner.Shared;

/// <summary>Куда положить дело. В JSON различается полем "kind".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SomedayTargetDto), "someday")]
[JsonDerivedType(typeof(DayTargetDto), "day")]
[JsonDerivedType(typeof(StepListTargetDto), "steps")]
[JsonDerivedType(typeof(WeekTargetDto), "week")]
public abstract record TaskTargetDto;

/// <summary>В ленту: категория (null — «неразобранное») и позиция (0 — наверх).</summary>
public sealed record SomedayTargetDto(int? CategoryId, int Index) : TaskTargetDto;

/// <summary>
/// В день. Index — позиция среди дел дня БЕЗ времени, считая без самого перетаскиваемого дела.
/// Дела со временем всегда стоят первыми, по времени.
/// </summary>
public sealed record DayTargetDto(DateOnly Date, int Index) : TaskTargetDto;

/// <summary>На неделю без дня (N4): WeekStart — любой день недели; RoleId — бросили на «на неделе» роли: дело получает и эту роль.</summary>
public sealed record WeekTargetDto(DateOnly WeekStart, int? RoleId = null) : TaskTargetDto;

/// <summary>В список шагов дела ParentId, без дня (на строку родителя или в его раскрытый список): позиция в списке.</summary>
public sealed record StepListTargetDto(int ParentId, int Index) : TaskTargetDto;

/// <summary>Новое дело; IsProject — сразу проект («+ проект» в «🛠 Проекты»).</summary>
public sealed record CreateTaskRequest(string Title, TaskTargetDto Target, bool IsProject = false);

/// <summary>«🛠 Сделать проектом» (true) / «🛠 больше не проект» (false).</summary>
public sealed record SetProjectRequest(bool IsProject);

/// <summary>«Проект» в карточке дела: ParentId — дело-проект, null — убрать из проекта.</summary>
public sealed record SetParentRequest(int? ParentId);

/// <summary>
/// StartTime / DurationMinutes = null — без времени / длительности. RoleIds — роли дела (null или пусто — без ролей).
/// У занятия цели роль и Quadrant берутся от цели.
/// </summary>
public sealed record UpdateTaskRequest(
    string Title, string? Notes, IReadOnlyList<int>? RoleIds, int? Quadrant, TimeOnly? StartTime, int? DurationMinutes);

/// <summary>ViaSessionId — шаг отмечают в строке или окне этого занятия цели: если отметка закончила проект цели, отмечается и занятие (R4).</summary>
public sealed record SetDoneRequest(bool IsDone, int? ViaSessionId = null);

public sealed record ReorderRequest(IReadOnlyList<int> Ids);

/// <summary>AtTop — новая категория встаёт в начало списка (кнопка «+ категория» сверху ленты), иначе — в конец.</summary>
public sealed record CreateCategoryRequest(string Name, string? Emoji, bool AtTop = false);

/// <summary>Раскрыть или свернуть все категории разом.</summary>
public sealed record SetAllExpandedRequest(bool IsExpanded);

/// <summary>null — поле не менять; Emoji = "" — убрать иконку.</summary>
public sealed record UpdateCategoryRequest(string? Name, string? Emoji, bool? IsExpanded);

public sealed record CreateRoleRequest(string Name, string? Color);

/// <summary>null — поле не менять; Vision "" — стереть «кем я хочу быть».</summary>
public sealed record UpdateRoleRequest(string? Name, string? Color, string? Vision = null);

/// <summary>IsRecurring — «каждую неделю»; null — как принято у роли (у измерений пилы — да, у обычных ролей — нет).</summary>
public sealed record CreateGoalRequest(int RoleId, DateOnly WeekStart, string Title, bool? IsRecurring = null);

/// <summary>Окно цели целиком. null у необязательных полей — «не задано»; IsRecurring / AllowSeveralPerDay = null — «каждую неделю» / «можно несколько раз в день» не менять.
/// Days — дни (маска, как в WeeklyGoalDto): null — не менять, 0 — «сам раскладываю». RoleIds — роли цели (хотя бы одна).
/// IsHabit — «🫙 копить» (null — не менять); ProjectId — проект новых занятий (null — не менять, 0 — без проекта).</summary>
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
    bool? IsHabit = null,
    int? ProjectId = null);

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

/// <summary>Настройки банки привычки: стартовое число (скрепки до привычки) и размер банки.</summary>
public sealed record UpdateHabitRequest(int StartCount, int JarSize);

/// <summary>«+1 сегодня». Today — сегодняшняя дата у пользователя.</summary>
public sealed record PlusOneRequest(DateOnly Today);

/// <summary>Каникулы с From по To включительно; HabitIds (id привычек) пусто или null — для всех привычек.</summary>
public sealed record CreateVacationRequest(DateOnly From, DateOnly To, IReadOnlyList<int>? HabitIds);

/// <summary>Новый шаг (в конец) или новое название шага.</summary>
public sealed record StepRequest(string Title);

/// <summary>Настройки: срок «давно лежат» (1–36 мес), миссия, её блок в панели. null — поле не менять; Mission "" — стереть.</summary>
public sealed record UpdateSettingsRequest(int? StaleMonths = null, string? Mission = null, bool? ShowMissionInPanel = null);

/// <summary>Сдвиг часов сервера (только Development, e2e).</summary>
public sealed record ShiftClockRequest(double Days);

/// <summary>Пропустить дело (skips.md, A2): Kind — "NotNeeded" | "Missed"; причина необязательна (до 200 знаков).</summary>
public sealed record SkipTaskRequest(string Kind, string? Reason = null);

/// <summary>Вход: прислать код на почту (docs/specs/accounts.md, L1).</summary>
public sealed record RequestCodeRequest(string Email);

/// <summary>Вход: проверить код из письма (L4).</summary>
public sealed record VerifyCodeRequest(string Email, string Code);

/// <summary>Приглашение по ссылке: токен из «…/invite#токен» (I4).</summary>
public sealed record InviteTokenRequest(string Token);

/// <summary>Регистрация по приглашению: код на эту почту (I4).</summary>
public sealed record InviteCodeRequest(string Token, string Email);

/// <summary>Регистрация по приглашению: код из письма (I4).</summary>
public sealed record InviteVerifyRequest(string Token, string Email, string Code);

/// <summary>Новое приглашение (I2): почта необязательна, срок — 1, 3, 7, 14 или 30 дней.</summary>
public sealed record CreateInviteRequest(string? Email, int Days = 7);

/// <summary>«Удалить аккаунт» (P3): подтверждение — своя почта.</summary>
public sealed record DeleteAccountRequest(string Email);

/// <summary>Отключить / включить пользователя (U3).</summary>
public sealed record SetUserDisabledRequest(bool IsDisabled);
