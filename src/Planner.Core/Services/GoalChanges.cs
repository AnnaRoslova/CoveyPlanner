using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Core.Services;

/// <summary>
/// Всё, что меняется в окне цели (RoleIds — роли цели, хотя бы одна). null у необязательных полей — «не задано»;
/// IsRecurring — «каждую неделю», AllowSeveralPerDay — «можно несколько раз в день» (null — не менять);
/// Days — по каким дням (null — не менять, Weekdays.None — «сам раскладываю»);
/// IsHabit — «🫙 копить» у постоянной цели (null — не менять; когда «каждую неделю» включают — по умолчанию: есть роль пилы);
/// ProjectId — проект новых занятий (null — не менять, 0 — без проекта).
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
    bool? IsHabit = null,
    int? ProjectId = null);

/// <summary>Цель после сохранения окна и занятия, убранные в корзину при раскладке по новым дням.</summary>
public sealed record GoalUpdate(WeeklyGoal Goal, IReadOnlyList<TaskItem> RemovedSessions);

/// <summary>«+ шаг» цели: цель, новый шаг её проекта и проект, если его создали этим шагом (G3).</summary>
public sealed record GoalStep(WeeklyGoal Goal, TaskItem Step, TaskItem? CreatedProject);
