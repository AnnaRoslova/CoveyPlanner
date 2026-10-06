using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Core.Services;

/// <summary>
/// Удаление цели вместе с судьбой её занятий (общее для удаления цели, цели → в ленту и удаления роли):
/// <list type="bullet">
/// <item>выполненные остаются делами (история); выполненное без дня встаёт в понедельник недели цели; у цели серии они
/// остаются скрепками этой серии (<see cref="TaskItem.ClipSeriesId"/>);</item>
/// <item>невыполненные в днях уходят в корзину;</item>
/// <item>невыполненное занятие без дня: мысль из ленты — обратно в ленту (в свою категорию или в «неразобранное»),
/// шаг проекта — остаётся шагом без дня у своего родителя.</item>
/// </list>
/// Сам не сохраняет изменения.
/// </summary>
public class GoalRemover(IGoalRepository goals, TaskPlacer placer, TimeProvider clock)
{
    public async Task RemoveAsync(WeeklyGoal goal, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var session in goal.Sessions.ToList())
        {
            goal.RemoveSession(session);

            switch (session)
            {
                case { State: TaskState.Planned, PlannedDate: null, IsStep: false }:
                    await placer.ReturnToSomedayAsync(session, ct);
                    break;

                case { State: TaskState.Planned, PlannedDate: not null }:
                    session.Trash(now);
                    break;

                case { State: TaskState.Done }:
                    if (session.PlannedDate is null)
                    {
                        // Отметка без дня (галочка цели другой недели) — в понедельник недели цели, чтобы не потерять скрепку
                        await placer.PlaceAsync(session, new DayTarget(goal.WeekStart, int.MaxValue), ct);
                    }

                    if (goal.SeriesId is { } seriesId)
                    {
                        session.KeepAsClip(seriesId);
                    }

                    break;
            }
        }

        goals.Remove(goal);
    }
}
