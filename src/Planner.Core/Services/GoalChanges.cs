using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Core.Services;

/// <summary>
/// Всё, что меняется в окне цели. null у необязательных полей — «не задано»;
/// IsRecurring — «каждую неделю», AllowSeveralPerDay — «можно несколько раз в день» (null — не менять);
/// Days — по каким дням (null — не менять, Weekdays.None — «сам раскладываю»).
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
    bool? AllowSeveralPerDay = null,
    Weekdays? Days = null);

/// <summary>Цель после сохранения окна и занятия, убранные в корзину при раскладке по новым дням.</summary>
public sealed record GoalUpdate(WeeklyGoal Goal, IReadOnlyList<TaskItem> RemovedSessions);
