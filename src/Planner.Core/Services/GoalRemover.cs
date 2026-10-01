using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Core.Services;

/// <summary>
/// Удаление цели вместе с судьбой её занятий (общее для удаления цели, цели → в ленту и удаления роли):
/// выполненные остаются в неделе обычными делами (история), невыполненные в днях уходят в корзину,
/// невыполненное занятие без дня (мысль из ленты) возвращается в ленту — в свою категорию или в «неразобранное».
/// Сам не сохраняет изменения.
/// </summary>
public class GoalRemover(IGoalRepository goals, ITaskRepository tasks, TaskPlacer placer, TimeProvider clock)
{
    public async Task RemoveAsync(WeeklyGoal goal, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var session in goal.Sessions.ToList())
        {
            goal.RemoveSession(session);

            switch (session)
            {
                case { State: TaskState.Planned, PlannedDate: null }:
                    await placer.ReturnToSomedayAsync(session, ct);
                    break;

                case { State: TaskState.Planned }:
                    session.Trash(now);
                    break;

                case { State: TaskState.Done, PlannedDate: null }:
                    // Отметка «сделано» без дня нигде не видна — без цели она ничего не значит
                    tasks.Remove(session);
                    break;
            }
        }

        goals.Remove(goal);
    }
}
