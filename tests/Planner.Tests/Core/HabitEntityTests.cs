using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;

namespace Planner.Tests.Core;

/// <summary>Привычка = постоянная серия пилы: банка серии, архив и удаление привычки; каникулы.</summary>
public class HabitEntityTests
{
    private static readonly DateOnly Monday = new(2026, 9, 28);

    private static GoalSeries RecurringSeries() =>
        GoalSeries.StartFrom(WeeklyGoal.Create(1, Monday, "Бег"), recurring: true);

    [Fact]
    public void Series_NewJar_Defaults_Validates()
    {
        var series = RecurringSeries();
        Assert.Equal((30, 0, false), (series.JarSize, series.StartCount, series.HasCustomJar));

        series.SetJar(12, 50);
        Assert.Equal((50, 12, true), (series.JarSize, series.StartCount, series.HasCustomJar));

        Assert.Throws<DomainException>(() => series.SetJar(0, 0));
        Assert.Throws<DomainException>(() => series.SetJar(-1, 30));
        Assert.Throws<DomainException>(() => series.SetJar(0, GoalSeries.MaxJarSize + 1));
    }

    [Fact]
    public void Series_Stopped_IsEnded_ResumedIsNot()
    {
        var series = RecurringSeries();
        Assert.False(series.IsEnded);

        series.StopAfter(Monday);
        Assert.True(series.IsEnded);

        series.RecurFrom(Monday.AddDays(14));
        Assert.False(series.IsEnded);
    }

    [Fact]
    public void DeleteHabit_ResetsJarAndStopsCollecting_SeriesKept()
    {
        var series = RecurringSeries();
        series.SetHabit(true);
        series.SetJar(7, 40);
        series.StopAfter(Monday);

        series.DeleteHabit();
        Assert.Equal((false, 30, 0), (series.IsHabit, series.JarSize, series.StartCount));
        Assert.Equal("Бег", series.Title); // сама серия (шаблон цели) не тронута

        // «Копить» решает окно цели, а не возобновление серии
        series.RecurFrom(Monday.AddDays(7));
        Assert.False(series.IsHabit);
    }

    [Fact]
    public void SetHabit_Off_KeepsJar()
    {
        var series = RecurringSeries();
        series.SetHabit(true);
        series.SetJar(7, 40);

        series.SetHabit(false);
        series.SetHabit(true);
        Assert.Equal((true, 40, 7), (series.IsHabit, series.JarSize, series.StartCount));
    }

    [Fact]
    public void Vacation_EmptyList_AppliesToAll_OtherwiseOnlyChosen()
    {
        var all = Vacation.Create(Monday, Monday.AddDays(13), []);
        var some = Vacation.Create(Monday, Monday, [2, 3]);

        Assert.True(all.AppliesTo(7));
        Assert.True(some.AppliesTo(2));
        Assert.False(some.AppliesTo(7));
        Assert.Throws<DomainException>(() => Vacation.Create(Monday, Monday.AddDays(-1), []));
    }

    [Fact]
    public void Vacation_RemoveHabit_LastOneMeansDelete()
    {
        var vacation = Vacation.Create(Monday, Monday, [2, 3]);

        Assert.True(vacation.RemoveHabit(2));
        Assert.True(vacation.RemoveHabit(9)); // не её привычка — ничего не меняется
        Assert.False(vacation.RemoveHabit(3)); // иначе пустой список стал бы «для всех»
    }
}
