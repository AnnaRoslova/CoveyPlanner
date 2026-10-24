using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Services;

namespace Planner.Tests.Core;

/// <summary>Переходы пропуска (docs/specs/skips.md, K3–K4) и счёт шагов (S1).</summary>
public class SkipRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2026, 10, 19);

    private static TaskItem InDay(DateOnly? day = null)
    {
        var task = TaskItem.CreatePlanned("Бег", Now);
        task.PlanForDay(day ?? Monday);
        return task;
    }

    [Fact]
    public void Skip_InDay_KeepsPlace_ReasonTrimmed()
    {
        var task = InDay();
        task.Skip(SkipKind.Missed, "  заболела ", Now);

        Assert.Equal((TaskState.Skipped, SkipKind.Missed, "заболела", Monday), (task.State, task.SkipKind, task.SkipReason, task.PlannedDate));
        Assert.True(task.IsInPlan);
        Assert.False(task.IsNotNeeded);
    }

    [Fact]
    public void Skip_OnWeekWithoutDay_IsAllowed()
    {
        var task = TaskItem.CreatePlanned("Позвонить", Now);
        task.PlanForWeekWithoutDay(Monday);
        task.Skip(SkipKind.NotNeeded, null, Now);
        Assert.True(task.IsNotNeeded);
    }

    [Fact]
    public void Skip_Thought_Done_Project_WithoutPlace_Throw()
    {
        Assert.Throws<DomainException>(() => TaskItem.CreateSomeday("Мысль", null, Now).Skip(SkipKind.NotNeeded, null, Now));

        var done = InDay();
        done.Complete(Now);
        Assert.Throws<DomainException>(() => done.Skip(SkipKind.Missed, null, Now));

        var project = InDay();
        project.MakeProject();
        Assert.Throws<DomainException>(() => project.Skip(SkipKind.NotNeeded, null, Now));

        Assert.Throws<DomainException>(() => TaskItem.CreatePlanned("Без места", Now).Skip(SkipKind.Missed, null, Now));
    }

    [Fact]
    public void Skip_ReasonLongerThan200_Throws() =>
        Assert.Throws<DomainException>(() => InDay().Skip(SkipKind.Missed, new string('я', 201), Now));

    [Fact]
    public void Unskip_BackToOpen_FieldsCleared()
    {
        var task = InDay();
        task.Skip(SkipKind.Missed, "забила", Now);
        task.Unskip();
        Assert.Equal((TaskState.Planned, (SkipKind?)null, (string?)null), (task.State, task.SkipKind, task.SkipReason));
    }

    [Fact]
    public void Complete_FromSkip_IsDone_SkipCleared()
    {
        var task = InDay();
        task.Skip(SkipKind.NotNeeded, "отменили", Now);
        task.Complete(Now);
        Assert.Equal((TaskState.Done, (SkipKind?)null), (task.State, task.SkipKind));
    }

    [Fact]
    public void PlanForDay_SameDay_KeepsSkip_OtherDay_Reopens()
    {
        var task = InDay();
        task.Skip(SkipKind.Missed, null, Now);
        task.PlanForDay(Monday);
        Assert.True(task.IsSkipped);

        task.PlanForDay(Monday.AddDays(1));
        Assert.Equal(TaskState.Planned, task.State);
    }

    [Fact]
    public void MoveToSomeday_ClearsSkip()
    {
        var task = InDay();
        task.Skip(SkipKind.Missed, null, Now);
        task.MoveToSomeday(null);
        Assert.Equal((TaskState.Someday, (SkipKind?)null), (task.State, task.SkipKind));
    }

    [Fact]
    public void Trash_AndRestore_KeepSkip()
    {
        var task = InDay();
        task.Skip(SkipKind.NotNeeded, "отменили", Now);
        task.Trash(Now);
        task.Restore();
        Assert.Equal((TaskState.Skipped, SkipKind.NotNeeded, "отменили"), (task.State, task.SkipKind, task.SkipReason));
    }

    [Fact]
    public void NotNeededStep_NotCountedInSummary()
    {
        var project = InDay();
        project.MakeProject();
        var done = TaskItem.CreateStep(project, "Покрасить", Now);
        done.Complete(Now);
        var notNeeded = TaskItem.CreateStep(project, "Плитка", Now);
        notNeeded.PlanForDay(Monday);
        notNeeded.Skip(SkipKind.NotNeeded, null, Now);
        var open = TaskItem.CreateStep(project, "Обои", Now);

        Assert.Equal(new ProjectSummary(1, 2, 0), ProjectRules.Summarize([done, notNeeded, open]));
        Assert.False(ProjectRules.ShouldAutoComplete(project, [done, notNeeded, open]));
        Assert.True(ProjectRules.ShouldAutoComplete(project, [done, notNeeded]));
    }
}
