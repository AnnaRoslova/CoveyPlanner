namespace Planner.Core.Entities;

/// <summary>
/// Связь дела с целью недели (таблица TaskGoals): дело — занятие этой цели и засчитывается в её прогресс.
/// Одно дело может засчитываться в несколько целей; первая по <see cref="Position"/> — «главная»:
/// от неё у дела шаблон (название, время, длительность, заметки) и квадрант.
/// Создаётся и снимается только через <see cref="WeeklyGoal.AddSession"/> / <see cref="WeeklyGoal.RemoveSession"/>.
/// </summary>
public class TaskGoal
{
    // Для EF Core
    private TaskGoal()
    {
    }

    internal TaskGoal(TaskItem task, WeeklyGoal goal, int position)
    {
        Task = task;
        Goal = goal;
        Position = position;
    }

    public int TaskId { get; private set; }

    public int GoalId { get; private set; }

    /// <summary>Порядок связей у дела: 0 — главная цель.</summary>
    public int Position { get; private set; }

    public TaskItem Task { get; private set; } = null!;

    public WeeklyGoal Goal { get; private set; } = null!;
}
