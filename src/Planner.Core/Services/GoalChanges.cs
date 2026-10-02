using Planner.Core.Enums;

namespace Planner.Core.Services;

/// <summary>
/// Всё, что меняется в окне цели. null у необязательных полей — «не задано»;
/// IsRecurring — «каждую неделю», AllowSeveralPerDay — «можно несколько раз в день» (null — не менять).
/// </summary>
public sealed record GoalChanges(
    string Title,
    int RoleId,
    int TimesPerWeek,
    TimeOnly? StartTime,
    Quadrant? Quadrant,
    string? Notes,
    int? DurationMinutes,
    bool? IsRecurring = null,
    bool? AllowSeveralPerDay = null);
