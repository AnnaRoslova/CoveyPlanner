using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;

namespace Planner.Tests.Core;

/// <summary>Шаги дела (дело-чеклист, один уровень).</summary>
public class StepTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Step_BelongsToParent_NoDayNoCategory_NoNesting()
    {
        var parent = TaskItem.CreateSomeday("Сарай", 5, Now);
        var step = TaskItem.CreateStep(parent, "Купить доски", Now);

        Assert.True(step.IsStep);
        Assert.Equal((TaskState.Planned, (int?)null, (DateOnly?)null), (step.State, step.CategoryId, step.PlannedDate));
        Assert.Throws<DomainException>(() => TaskItem.CreateStep(step, "Шаг шага", Now));

        step.Complete(Now);
        Assert.Equal(TaskState.Done, step.State);
    }

    [Fact]
    public void Step_TrashedParent_NoNewSteps()
    {
        var parent = TaskItem.CreatePlanned("Сарай", Now);
        parent.Trash(Now);

        Assert.Throws<DomainException>(() => TaskItem.CreateStep(parent, "Шаг", Now));
    }
}
