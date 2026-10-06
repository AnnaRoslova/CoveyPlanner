using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Services;

namespace Planner.Tests.Core;

/// <summary>Правила проекта (S1, S7, S12, S13, S17): сводка «☑ N/M», автоотметка, один уровень.</summary>
public class ProjectRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static (TaskItem Parent, List<TaskItem> Steps) Project(int steps)
    {
        var parent = TaskItem.CreatePlanned("Ремонт", Now);
        return (parent, Enumerable.Range(0, steps).Select(i => TaskItem.CreateStep(parent, $"Шаг {i}", Now)).ToList());
    }

    private static TaskItem Session()
    {
        var goal = WeeklyGoal.Create(1, Monday, "Чтение");
        var session = TaskItem.CreatePlanned("Чтение", Now);
        goal.AddSession(session);
        return session;
    }

    [Fact]
    public void Summary_CountsStepsWithoutSessionsAndTrash()
    {
        var (_, steps) = Project(3);
        steps[0].Complete(Now);
        steps[2].Trash(Now);
        var session = Session();
        session.Complete(Now);

        Assert.Equal(new ProjectSummary(1, 2, 1), ProjectRules.Summarize([.. steps, session]));
        Assert.Null(ProjectRules.Summarize([steps[2]]));
    }

    [Fact]
    public void AutoComplete_OnlyWhenAllStepsDone_SessionsIgnored_DoneParentUntouched()
    {
        var (parent, steps) = Project(2);
        steps[0].Complete(Now);
        Assert.False(ProjectRules.ShouldAutoComplete(parent, steps));

        steps[1].Complete(Now);
        var open = Session();
        Assert.True(ProjectRules.ShouldAutoComplete(parent, [.. steps, open])); // S17: занятия не мешают

        Assert.False(ProjectRules.ShouldAutoComplete(parent, [open])); // проект только из занятий — сам не отмечается
        parent.Complete(Now);
        Assert.False(ProjectRules.ShouldAutoComplete(parent, steps));
    }

    [Fact]
    public void SetParent_FeedThoughtBecomesUndatedStep_OneLevel_NoSelf()
    {
        var parent = TaskItem.CreateSomeday("Ремонт", 5, Now);
        var thought = TaskItem.CreateSomeday("Краска", 5, Now);
        thought.SetParent(parent);
        Assert.Equal((TaskState.Planned, (DateOnly?)null, 5), (thought.State, thought.PlannedDate, thought.CategoryId));

        Assert.Throws<DomainException>(() => parent.SetParent(thought)); // шаг не бывает проектом
        Assert.Throws<DomainException>(() => parent.SetParent(parent));

        thought.MoveToSomeday(null); // S21: в ленту — уже не шаг
        Assert.False(thought.IsStep);
    }

    [Fact]
    public void KeepAsClip_ClearedWhenSessionOfGoalOrBackToFeed()
    {
        var task = TaskItem.CreatePlanned("Бег", Now);
        task.KeepAsClip(7);
        Assert.Equal(7, task.ClipSeriesId);

        WeeklyGoal.Create(1, Monday, "Бег").AddSession(task);
        Assert.Null(task.ClipSeriesId);
    }
}
