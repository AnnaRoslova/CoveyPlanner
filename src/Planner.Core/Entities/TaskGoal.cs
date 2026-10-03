namespace Planner.Core.Entities;

/// <summary>
/// Связь дела с целью недели (таблица TaskGoals): дело — занятие этой цели и засчитывается в её прогресс.
/// У дела — не больше одной цели (<see cref="Position"/> всегда 0; колонка осталась от времени, когда целей могло быть несколько).
/// Создаётся и снимается только через <see cref="WeeklyGoal.AddSession"/> / <see cref="WeeklyGoal.RemoveSession"/>.
/// </summary>
public class TaskGoal
{
    // Для EF Core
    private TaskGoal()
    {
    }

    internal TaskGoal(TaskItem task, WeeklyGoal goal)
    {
        Task = task;
        Goal = goal;
    }

    public int TaskId { get; private set; }

    public int GoalId { get; private set; }

    public int Position { get; private set; }

    public TaskItem Task { get; private set; } = null!;

    public WeeklyGoal Goal { get; private set; } = null!;
}
