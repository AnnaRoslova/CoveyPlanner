using Planner.Core.Enums;

namespace Planner.Core.Services;

/// <summary>Всё, что меняется в окне цели. null у необязательных полей — «не задано».</summary>
public sealed record GoalChanges(
    string Title,
    int RoleId,
    int TimesPerWeek,
    TimeOnly? StartTime,
    Quadrant? Quadrant,
    string? Notes,
    int? DurationMinutes);
