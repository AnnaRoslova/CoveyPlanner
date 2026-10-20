using Planner.Client.Services;
using Planner.Shared;

namespace Planner.Tests.Client;

/// <summary>Подсветка дела «на неделе» без дня (docs/specs/fixes-2026-10.md, E2): как недособранная цель (N1).</summary>
public class WeekUnassignedTests
{
    private static readonly DateOnly Monday = new(2026, 10, 19);

    private static TaskDto Task(TaskStateDto state = TaskStateDto.Planned) =>
        new(1, "Купить подарок", null, null, [1], null, null, state, null, null, null, 0, null, PlannedWeek: Monday);

    [Fact]
    public void OpenTask_CurrentWeek_Highlighted() // E2.1
    {
        Assert.True(PlannerState.IsUnassigned(Task(), Monday, Monday.AddDays(2)));
        Assert.True(PlannerState.IsUnassigned(Task(), Monday, Monday.AddDays(6))); // воскресенье — неделя ещё идёт
    }

    [Fact]
    public void FutureWeek_Highlighted_PastWeek_Not() // E2.3
    {
        Assert.True(PlannerState.IsUnassigned(Task(), Monday, Monday.AddDays(-3)));
        Assert.False(PlannerState.IsUnassigned(Task(), Monday, Monday.AddDays(7)));
    }

    [Fact]
    public void DoneTask_NotHighlighted() => // E2.4
        Assert.False(PlannerState.IsUnassigned(Task(TaskStateDto.Done), Monday, Monday));

    [Fact]
    public void TaskWithDay_NotHighlighted() => // E2.2
        Assert.False(PlannerState.IsUnassigned(Task() with { PlannedDate = Monday }, Monday, Monday));
}
