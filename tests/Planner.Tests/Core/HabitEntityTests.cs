using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;

namespace Planner.Tests.Core;

/// <summary>Копилка, каникулы и копилка как часть шаблона цели.</summary>
public class HabitEntityTests
{
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateTime Now = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Habit_Validates_NormJarAndStartCount()
    {
        var habit = Habit.Create(" Тренировки ", "💪", "#9FD3BD", 3, 30, 0, Now);
        Assert.Equal(("Тренировки", "#9fd3bd"), (habit.Name, habit.Color));

        Assert.Throws<DomainException>(() => Habit.Create("x", null, "#9fd3bd", 0, 30, 0, Now));
        Assert.Throws<DomainException>(() => Habit.Create("x", null, "#9fd3bd", 3, 0, 0, Now));
        Assert.Throws<DomainException>(() => Habit.Create("x", null, "#9fd3bd", 3, 30, -1, Now));
        Assert.Throws<DomainException>(() => Habit.Create("x", null, "#000000", 3, 30, 0, Now)); // цвет не из палитры
        Assert.Throws<DomainException>(() => Habit.Create(" ", null, "#9fd3bd", 3, 30, 0, Now));
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
        Assert.True(vacation.RemoveHabit(9)); // не её копилка — ничего не меняется
        Assert.False(vacation.RemoveHabit(3)); // иначе пустой список стал бы «для всех»
    }

    [Fact]
    public void Goal_SessionsInheritHabit_AndFollowItsChange_ExceptOwn()
    {
        var goal = WeeklyGoal.Create(1, Monday, "Бег");
        goal.SetHabit(5);
        var done = Session(goal);
        done.Complete(Now);
        var planned = Session(goal);
        var own = Session(goal);
        own.SetHabit(9); // своя копилка

        goal.SetHabit(6);

        Assert.Equal([6, 6, 9], new[] { done, planned, own }.Select(t => t.HabitId));
        Assert.Equal(TaskState.Done, done.State); // выполненное переехало со скрепкой
    }

    [Fact]
    public void Goal_CopiesAndSeries_KeepHabit()
    {
        var goal = WeeklyGoal.Create(1, Monday, "Бег");
        goal.SetHabit(5);

        var series = GoalSeries.StartFrom(goal, recurring: true);
        Assert.Equal(5, series.HabitId);
        Assert.Equal(5, WeeklyGoal.FromSeries(series, Monday.AddDays(7)).HabitId);
        Assert.Equal(5, goal.CopyForWeek(Monday.AddDays(14)).HabitId);
    }

    [Fact]
    public void AddSession_GoalWithHabit_TaskJoinsIt_GoalWithout_KeepsTasksOwn()
    {
        var withHabit = WeeklyGoal.Create(1, Monday, "Бег");
        withHabit.SetHabit(5);
        var task = TaskItem.CreatePlanned("Пробежка", Now);
        task.SetHabit(9);
        withHabit.AddSession(task);
        Assert.Equal(5, task.HabitId);

        var without = WeeklyGoal.Create(1, Monday, "Растяжка");
        without.AddSession(task);
        Assert.Equal(5, task.HabitId);
    }

    [Fact]
    public void GoalFromTask_TakesTasksHabit()
    {
        var task = TaskItem.CreateSomeday("Медитация", null, Now);
        task.SetHabit(4);
        var goal = WeeklyGoal.Create(1, Monday, "Медитация");

        goal.CopyTemplateFrom(task);

        Assert.Equal(4, goal.HabitId);
    }

    private static TaskItem Session(WeeklyGoal goal)
    {
        var task = TaskItem.CreatePlanned(goal.Title, Now);
        goal.FillFromTemplate(task);
        goal.AddSession(task);
        task.PlanForDay(Monday);
        return task;
    }
}
