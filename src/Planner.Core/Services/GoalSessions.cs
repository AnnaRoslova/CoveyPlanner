using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>
/// Занятия целей: новое занятие по шаблону цели, постановка в день и раскладка по дням постоянной цели
/// (<see cref="WeeklyGoal.Days"/>). Общий помощник для сервисов целей и серий. Сам не сохраняет изменения.
/// </summary>
public class GoalSessions(ITaskRepository tasks, TaskPlacer placer, ProjectService projects, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Новое занятие по шаблону цели (без дня). Если у цели выбран проект и он открыт (не выполнен, не в корзине), занятие —
    /// его шаг в конце списка (P2–P3); иначе — без проекта.
    /// </summary>
    public async Task<TaskItem> CreateAsync(WeeklyGoal goal, CancellationToken ct = default)
    {
        var session = TaskItem.CreatePlanned(goal.Title, Now);
        goal.FillFromTemplate(session);
        tasks.Add(session);
        goal.AddSession(session);

        if (goal.ProjectId is { } projectId
            && await tasks.FindAsync(projectId, ct) is { State: TaskState.Someday or TaskState.Planned, IsStep: false } project)
        {
            session.SetParent(project);
            await projects.PlaceStepAsync(session, projectId, int.MaxValue, ct);
        }

        return session;
    }

    /// <summary>Занятие → день; без своего времени оно берёт время цели.</summary>
    public async Task PlaceInDayAsync(WeeklyGoal goal, TaskItem session, DayTarget day, CancellationToken ct = default)
    {
        await placer.PlaceAsync(session, day, ct);
        if (session.StartTime is null && goal.StartTime is not null)
        {
            session.SetTime(goal.StartTime);
        }
    }

    /// <summary>
    /// Разложить занятия цели по её дням (если дни выбраны): в выбранный день без занятия — новое невыполненное
    /// (в конец дня или по времени), невыполненные занятия в невыбранные дни — в корзину; выполненные не трогаются.
    /// Возвращает занятия, убранные в корзину.
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>> ArrangeByDaysAsync(WeeklyGoal goal, CancellationToken ct = default)
    {
        if (goal.Days is not { } days)
        {
            return [];
        }

        var removed = new List<TaskItem>();
        foreach (var date in Week.Days(goal.WeekStart))
        {
            var onDay = goal.ActiveSessions.Where(t => t.PlannedDate == date).ToList();
            if (days.Includes(date))
            {
                if (onDay.Count == 0)
                {
                    await PlaceInDayAsync(goal, await CreateAsync(goal, ct), new DayTarget(date, int.MaxValue), ct);
                }
            }
            else
            {
                foreach (var session in onDay.Where(t => t.State == TaskState.Planned))
                {
                    session.Trash(Now);
                    removed.Add(session);
                }
            }
        }

        return removed;
    }
}
