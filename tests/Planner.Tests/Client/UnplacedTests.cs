using Planner.Client.Services;
using Planner.Shared;

namespace Planner.Tests.Client;

/// <summary>Подсветка неразложенного (docs/specs/next-batch.md, N1): «раз в неделю» минус занятия в днях недели и выполненные без дня.</summary>
public class UnplacedTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);

    private static WeeklyGoalDto Goal(int timesPerWeek, params GoalSessionDto[] sessions) =>
        new(1, [new GoalRoleDto(1, 0)], Monday, "Отчёт", timesPerWeek, null, null, null, null, sessions);

    [Fact]
    public void NoSessions_AllUnplaced() => Assert.Equal(2, PlannerState.UnplacedOf(Goal(2), Wednesday));

    [Fact]
    public void TwoInDays_OneDoneWithoutDay_Placed()
    {
        var goal = Goal(3, new(1, Monday, false), new(2, Wednesday, false), new(3, null, true));

        Assert.Equal(0, PlannerState.UnplacedOf(goal, Wednesday));
    }

    [Fact]
    public void OpenSessionWithoutDay_IsUnplaced()
    {
        var goal = Goal(1, new GoalSessionDto(1, null, false)); // мысль из ленты, ставшая целью, — пока не в дне

        Assert.Equal(1, PlannerState.UnplacedOf(goal, Wednesday));
    }

    [Fact]
    public void SeveralInOneDay_AllCount() // N1.5: «можно несколько раз в день»
    {
        var goal = Goal(2, new(1, Wednesday, false), new(2, Wednesday, false));

        Assert.Equal(0, PlannerState.UnplacedOf(goal, Wednesday));
    }

    [Fact]
    public void SessionOnAnotherWeek_NotCounted()
    {
        var goal = Goal(1, new GoalSessionDto(1, Monday.AddDays(7), false));

        Assert.Equal(1, PlannerState.UnplacedOf(goal, Wednesday));
    }

    [Fact]
    public void DoneGoal_NoHighlight() // норма закрыта галочкой цели
    {
        var goal = Goal(2, new(1, null, true), new(2, null, true));

        Assert.Equal(0, PlannerState.UnplacedOf(goal, Wednesday));
    }

    [Fact]
    public void PastWeek_NoHighlight_FutureWeek_Highlighted() // N1.4
    {
        Assert.Equal(0, PlannerState.UnplacedOf(Goal(2), Monday.AddDays(7)));
        Assert.Equal(2, PlannerState.UnplacedOf(Goal(2), Monday.AddDays(-3)));
        Assert.Equal(2, PlannerState.UnplacedOf(Goal(2), Monday.AddDays(6))); // воскресенье — неделя ещё идёт
    }
}
