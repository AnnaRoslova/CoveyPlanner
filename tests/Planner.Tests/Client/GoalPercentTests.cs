using Planner.Shared;

namespace Planner.Tests.Client;

/// <summary>Процент у цели в левой панели: выполненные занятия недели от нормы «раз в неделю».</summary>
public class GoalPercentTests
{
    [Theory]
    [InlineData(2, 1, 50)]   // «2 раза», одно выполнено
    [InlineData(1, 0, 0)]    // разовая цель: 0% / 100%
    [InlineData(1, 1, 100)]
    [InlineData(3, 2, 66)]   // вниз до целого: 100% — только при выполненной норме
    [InlineData(3, 5, 100)]  // сверх нормы — не больше 100
    [InlineData(7, 0, 0)]
    public void Percent_DoneSessionsOfWeeklyNorm(int timesPerWeek, int done, int expected)
    {
        var sessions = Enumerable.Range(1, done + 1)
            .Select(i => new GoalSessionDto(i, null, IsDone: i <= done)) // и одно невыполненное — не считается
            .ToList();
        var goal = new WeeklyGoalDto(1, [new GoalRoleDto(1, 0)], new DateOnly(2026, 10, 5), "Тренировка", timesPerWeek,
            null, null, null, null, sessions);

        Assert.Equal(expected, goal.Percent);
    }
}
