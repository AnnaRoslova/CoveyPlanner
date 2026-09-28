using Planner.Core.Entities;
using Planner.Core.Enums;
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
        t.RoleId,
        (int?)t.Quadrant,
        (TaskStateDto)t.State,
        t.PlannedDate,
        t.StartTime,
        t.SortOrder,
        t.TrashedAt);

    public static CategoryDto ToDto(this Category c) => new(c.Id, c.Name, c.Emoji, c.SortOrder, c.IsExpanded);

    public static RoleDto ToDto(this Role r) =>
        new(r.Id, r.Name, r.Color, r.SortOrder, (SawDimensionDto?)r.SawDimension);

    public static WeeklyGoalDto ToDto(this WeeklyGoal g) => new(
        g.Id,
        g.RoleId,
        g.WeekStart,
        g.Title,
        g.IsDone,
        g.SortOrder,
        g.LinkedTask?.Id ?? g.TaskId,
        g.LinkedTask?.PlannedDate,
        g.LinkedTask?.StartTime);

    public static GoalResultDto ToResultDto(this WeeklyGoal g) => new(g.ToDto(), g.LinkedTask?.ToDto());

    public static TaskTarget ToCore(this TaskTargetDto target) => target switch
    {
        SomedayTargetDto s => new SomedayTarget(s.CategoryId, s.Index),
        DayTargetDto d => new DayTarget(d.Date, d.Index),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Неизвестное место назначения."),
    };

    public static Quadrant? ToQuadrant(this int? value) => (Quadrant?)value;
}
