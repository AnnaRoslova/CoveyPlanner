using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>Результат «сделать дело занятием цели»: дело, затронутые цели (прежняя и новая) и занятия, которые дело заменило (они в корзине).</summary>
public sealed record TaskGoalUpdate(TaskItem Task, IReadOnlyList<WeeklyGoal> Goals, IReadOnlyList<TaskItem> Replaced);

/// <summary>Чьё занятие дело: перетаскивание дела на строку цели делает его занятием этой цели (у дела — одна цель).</summary>
public class TaskGoalService(
    ITaskRepository tasks,
    IGoalRepository goals,
    TaskPlacer placer,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>
    /// Сделать дело занятием цели <paramref name="goalId"/> (null — отвязать). Было занятием другой цели — переходит к этой
    /// (у прежней прогресс уменьшается). Цель — той же недели, что и дело. Мысль из ленты становится занятием без дня;
    /// отвязали занятие без дня — оно возвращается в ленту.
    /// «Одно занятие цели в день» (если у цели не разрешено несколько): другое невыполненное занятие цели в тот же день
    /// заменяется этим делом (уходит в корзину), выполненное — дело не привязывается («Уже есть в этот день.»).
    /// </summary>
    public async Task<TaskGoalUpdate> SetGoalAsync(int taskId, int? goalId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Дело", taskId);
        if (task.State == TaskState.Trashed)
        {
            throw new DomainException("Дело в корзине.");
        }

        // Прежняя цель загружается — тогда связь с ней снимается через неё саму (и её прогресс виден в ответе)
        var old = task.GoalId is { } oldId ? await goals.FindAsync(oldId, ct) : null;
        var goal = goalId is { } id ? await goals.FindAsync(id, ct) ?? throw new NotFoundException("Цель", id) : null;
        if (goal == old)
        {
            return new TaskGoalUpdate(task, goal is null ? [] : [goal], []);
        }

        var replaced = new List<TaskItem>();
        if (goal is not null)
        {
            if (task.PlannedDate is { } date && Week.StartOf(date) != goal.WeekStart)
            {
                throw new DomainException("Занятием можно сделать только дело той же недели, что и цель.");
            }

            if (task.PlannedDate is { } day && !goal.AllowSeveralPerDay)
            {
                var others = goal.ActiveSessions.Where(t => t.PlannedDate == day && t != task).ToList();
                if (others.Any(t => t.State == TaskState.Done))
                {
                    throw new DomainException(WeeklyGoal.DayTakenMessage);
                }

                replaced.AddRange(others);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var other in replaced)
            {
                other.Trash(now); // в корзине занятие остаётся связанным с целью, но не считается
            }

            if (task.State == TaskState.Someday)
            {
                task.PlanForWeek(); // мысль из ленты — занятие без дня
            }

            goal.AddSession(task); // от прежней цели дело отвязывается само
        }
        else
        {
            task.DetachFromGoal();
            if (task.State == TaskState.Planned && task.PlannedDate is null)
            {
                await placer.ReturnToSomedayAsync(task, ct); // занятие без дня без цели нигде не видно — в ленту
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new TaskGoalUpdate(task, new[] { old, goal }.OfType<WeeklyGoal>().ToList(), replaced);
    }
}
