using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Habits;
using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Преобразования сущностей Core ↔ контракты Shared.</summary>
public static class Mapping
{
    public static TaskDto ToDto(this TaskItem t) => new(
        t.Id,
        t.Title,
        t.Notes,
        t.CategoryId,
        t.RoleIds,
        t.GoalId,
        (int?)t.Quadrant,
        (TaskStateDto)t.State,
        t.PlannedDate,
        t.StartTime,
        t.DurationMinutes,
        t.SortOrder,
        t.TrashedAt,
        t.HabitId);

    public static CategoryDto ToDto(this Category c) => new(c.Id, c.Name, c.Emoji, c.SortOrder, c.IsExpanded);

    public static RoleDto ToDto(this Role r) =>
        new(r.Id, r.Name, r.Color, r.SortOrder, (SawDimensionDto?)r.SawDimension);

    public static WeeklyGoalDto ToDto(this WeeklyGoal g) => new(
        g.Id,
        g.Roles.OrderBy(r => r.RoleId).Select(r => new GoalRoleDto(r.RoleId, r.SortOrder)).ToList(),
        g.WeekStart,
        g.Title,
        g.TimesPerWeek,
        g.StartTime,
        (int?)g.Quadrant,
        g.Notes,
        g.DurationMinutes,
        g.ActiveSessions
            .OrderBy(t => t.PlannedDate ?? DateOnly.MaxValue).ThenBy(t => t.Id)
            .Select(t => new GoalSessionDto(t.Id, t.PlannedDate, t.State == TaskState.Done))
            .ToList(),
        g.RepeatsAfterThisWeek,
        g.AllowSeveralPerDay,
        (int?)g.Days,
        g.HabitId);

    /// <summary>Цель, её занятия, стоящие в днях (их поля могли поменяться по шаблону), и убранные при раскладке по дням.</summary>
    public static GoalUpdateResultDto ToUpdateResultDto(this GoalUpdate update) =>
        new(update.Goal.ToDto(),
            update.Goal.ActiveSessions.Where(t => t.PlannedDate is not null).Select(t => t.ToDto()).ToList(),
            update.RemovedSessions.Select(t => t.Id).ToList());

    public static GoalResultDto ToResultDto(this (WeeklyGoal Goal, TaskItem Session) result) =>
        new(result.Goal.ToDto(), result.Session.ToDto());

    public static HabitDto ToDto(this HabitOverview o) => new(
        o.Habit.Id,
        o.Habit.Name,
        o.Habit.Emoji,
        o.Habit.Color,
        o.Habit.WeeklyNorm,
        o.Habit.JarSize,
        o.Habit.StartCount,
        o.Habit.SortOrder,
        o.Habit.IsArchived,
        o.Habit.Unit,
        o.Stats.ToDto());

    public static HabitStatsDto ToDto(this HabitStats s) => new(
        s.Total,
        s.FullJars,
        s.InJar,
        s.ThisWeek,
        s.WeekStreak,
        s.WeekPaused,
        s.MonthStreak,
        s.MonthPaused,
        s.Weeks.Select(w => new HabitWeekDto(w.WeekStart, w.Count, (PeriodResultDto)w.Result)).ToList(),
        s.Months.Select(m => new HabitMonthDto(m.Month, (PeriodResultDto)m.Result)).ToList(),
        s.AveragePerWeek,
        s.ThisMonth,
        s.ThisYear,
        s.VacationUntil);

    public static VacationDto ToDto(this Vacation v) => new(v.Id, v.From, v.To, v.HabitIds);

    public static HabitFields ToFields(this SaveHabitRequest r) =>
        new(r.Name, r.Emoji, r.Color, r.WeeklyNorm, r.JarSize, r.StartCount);

    public static TaskTarget ToCore(this TaskTargetDto target) => target switch
    {
        SomedayTargetDto s => new SomedayTarget(s.CategoryId, s.Index),
        DayTargetDto d => new DayTarget(d.Date, d.Index),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Неизвестное место назначения."),
    };

    public static Quadrant? ToQuadrant(this int? value) => (Quadrant?)value;
}
