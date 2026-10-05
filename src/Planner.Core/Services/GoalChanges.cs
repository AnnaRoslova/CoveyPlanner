using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Core.Services;

/// <summary>
/// Всё, что меняется в окне цели (RoleIds — роли цели, хотя бы одна). null у необязательных полей — «не задано»;
/// IsRecurring — «каждую неделю», AllowSeveralPerDay — «можно несколько раз в день» (null — не менять);
/// Days — по каким дням (null — не менять, Weekdays.None — «сам раскладываю»);
/// HabitId — копилка (null — не менять, 0 — без копилки).
/// </summary>
public sealed record GoalChanges(
    string Title,
    IReadOnlyList<int> RoleIds,
    int TimesPerWeek,
    TimeOnly? StartTime,
    Quadrant? Quadrant,
    string? Notes,
    int? DurationMinutes,
    bool? IsRecurring = null,
    bool? AllowSeveralPerDay = null,
    Weekdays? Days = null,
    int? HabitId = null);

/// <summary>Цель после сохранения окна и занятия, убранные в корзину при раскладке по новым дням.</summary>
public sealed record GoalUpdate(WeeklyGoal Goal, IReadOnlyList<TaskItem> RemovedSessions);
