using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;

namespace Planner.Tests.Core;

public class TaskItemTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public void CreateSomeday_TrimsTitle_AndKeepsCategory()
    {
        var task = TaskItem.CreateSomeday("  Купить краски  ", categoryId: 5, Now);

        Assert.Equal("Купить краски", task.Title);
        Assert.Equal(5, task.CategoryId);
        Assert.Equal(TaskState.Someday, task.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateSomeday_EmptyTitle_Throws(string title) =>
        Assert.Throws<DomainException>(() => TaskItem.CreateSomeday(title, 1, Now));

    [Fact]
    public void PlanForDay_FromSomeday_BecomesPlanned_AndKeepsCategory()
    {
        var task = TaskItem.CreateSomeday("Дело", 5, Now);

        task.PlanForDay(Monday);

        Assert.Equal(TaskState.Planned, task.State);
        Assert.Equal(Monday, task.PlannedDate);
        Assert.True(task.IsOnDay);
        Assert.Equal(5, task.CategoryId);
    }

    [Fact]
    public void SetTime_OnDay_KeepsHoursAndMinutes()
    {
        var task = TaskItem.CreatePlanned("Стоматолог", Now);
        task.PlanForDay(Monday);

        task.SetTime(new TimeOnly(15, 0, 42));

        Assert.Equal(new TimeOnly(15, 0), task.StartTime);
    }

    [Fact]
    public void SetTime_NotOnDay_Throws()
    {
        var someday = TaskItem.CreateSomeday("Мысль", 5, Now);
        var weekOnly = TaskItem.CreatePlanned("Цель без дня", Now);
        weekOnly.PlanForWeek();

        Assert.Throws<DomainException>(() => someday.SetTime(new TimeOnly(9, 0)));
        Assert.Throws<DomainException>(() => weekOnly.SetTime(new TimeOnly(9, 0)));
    }

    [Fact]
    public void MovingBetweenDays_KeepsTime()
    {
        var task = TaskItem.CreatePlanned("Встреча", Now);
        task.PlanForDay(Monday);
        task.SetTime(new TimeOnly(15, 0));

        task.PlanForDay(Monday.AddDays(2));

        Assert.Equal(new TimeOnly(15, 0), task.StartTime);
    }

    [Fact]
    public void MoveToSomeday_ClearsDateTimeAndDone()
    {
        var task = TaskItem.CreateSomeday("Дело", 5, Now);
        task.PlanForDay(Monday);
        task.SetTime(new TimeOnly(9, 0));
        task.Complete(Now);

        task.MoveToSomeday(5);

        Assert.Equal(TaskState.Someday, task.State);
        Assert.Null(task.PlannedDate);
        Assert.Null(task.StartTime);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void PlanForWeek_ClearsDayAndTime()
    {
        var task = TaskItem.CreatePlanned("Дело", Now);
        task.PlanForDay(Monday);
        task.SetTime(new TimeOnly(9, 0));

        task.PlanForWeek();

        Assert.Null(task.PlannedDate);
        Assert.Null(task.StartTime);
        Assert.False(task.IsOnDay);
    }

    [Fact]
    public void Moving_DoneTask_KeepsItDone()
    {
        var task = TaskItem.CreatePlanned("Дело", Now);
        task.PlanForDay(Monday);
        task.Complete(Now);

        task.PlanForDay(Monday.AddDays(2));

        Assert.Equal(TaskState.Done, task.State);
    }

    [Fact]
    public void Complete_SomedayTask_Throws()
    {
        var task = TaskItem.CreateSomeday("Дело", 5, Now);

        Assert.Throws<DomainException>(() => task.Complete(Now));
    }

    [Fact]
    public void CompleteAndUncomplete_TogglesState()
    {
        var task = TaskItem.CreatePlanned("Дело", Now);
        task.PlanForDay(Monday);

        task.Complete(Now);
        Assert.Equal(TaskState.Done, task.State);
        Assert.Equal(Now, task.CompletedAt);

        task.Uncomplete();
        Assert.Equal(TaskState.Planned, task.State);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void TrashAndRestore_ReturnsToPreviousPlace()
    {
        var task = TaskItem.CreateSomeday("Дело", 5, Now);
        task.PlanForDay(Monday);
        task.SetTime(new TimeOnly(9, 30));

        task.Trash(Now);
        Assert.Equal(TaskState.Trashed, task.State);
        Assert.Equal(TaskState.Planned, task.PreviousState);
        Assert.Throws<DomainException>(() => task.PlanForDay(Monday));

        task.Restore();
        Assert.Equal(TaskState.Planned, task.State);
        Assert.Equal(Monday, task.PlannedDate);
        Assert.Equal(new TimeOnly(9, 30), task.StartTime);
        Assert.Null(task.TrashedAt);
    }

    [Fact]
    public void Restore_NotTrashed_Throws()
    {
        var task = TaskItem.CreateSomeday("Дело", 5, Now);

        Assert.Throws<DomainException>(task.Restore);
    }
}
