using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>Результат «засчитать дело в цели»: дело, затронутые цели и занятия, которые дело заменило (они в корзине).</summary>
public sealed record TaskGoalsUpdate(TaskItem Task, IReadOnlyList<WeeklyGoal> Goals, IReadOnlyList<TaskItem> Replaced);

/// <summary>
/// В какие цели недели засчитывается дело (одно дело — несколько целей: «Прогулка в парке» — и «10к шагов», и «Природа»).
/// Связывают перетаскиванием дела на строку цели и в окне дела.
/// </summary>
public class TaskGoalService(
    ITaskRepository tasks,
    IGoalRepository goals,
    GoalSessions sessions,
    TaskPlacer placer,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>
    /// Задать набор целей дела (порядок — порядок новых связей; уже связанные сохраняют место, первая — главная).
    /// Цели — той же недели, что и дело. Мысль из ленты становится занятием без дня; отвязали последнюю цель
    /// у занятия без дня — оно возвращается в ленту.
    /// «Одно занятие цели в день» (если у цели не разрешено несколько): другое невыполненное занятие цели в тот же день
    /// заменяется этим делом (уходит в корзину), выполненное — связь не создаётся («Уже есть в этот день.»).
    /// </summary>
    public async Task<TaskGoalsUpdate> SetGoalsAsync(int taskId, IReadOnlyList<int> goalIds, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Дело", taskId);
        if (task.State == TaskState.Trashed)
        {
            throw new DomainException("Дело в корзине.");
        }

        var current = await sessions.LoadGoalsOfAsync(task, ct);
        var wanted = new List<WeeklyGoal>();
        foreach (var goalId in goalIds.Distinct())
        {
            wanted.Add(current.FirstOrDefault(g => g.Id == goalId)
                       ?? await goals.FindAsync(goalId, ct)
                       ?? throw new NotFoundException("Цель", goalId));
        }

        var week = task.PlannedDate is { } date ? Week.StartOf(date) : wanted.FirstOrDefault()?.WeekStart;
        if (wanted.Any(g => g.WeekStart != week))
        {
            throw new DomainException("Засчитать можно только в цели той же недели, что и дело.");
        }

        // Сначала проверяем все новые цели, потом меняем: отказ не должен оставить половину связей
        var added = wanted.Where(g => !current.Contains(g)).ToList();
        var replaced = new List<TaskItem>();
        foreach (var goal in added)
        {
            if (task.PlannedDate is not { } day || goal.AllowSeveralPerDay)
            {
                continue;
            }

            var others = goal.ActiveSessions.Where(t => t.PlannedDate == day && t != task).ToList();
            if (others.Any(t => t.State == TaskState.Done))
            {
                throw new DomainException(WeeklyGoal.DayTakenMessage);
            }

            replaced.AddRange(others.Where(t => !replaced.Contains(t)));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var old in replaced)
        {
            old.Trash(now); // в корзине занятие остаётся связанным с целями, но не считается
        }

        foreach (var goal in current.Where(g => !wanted.Contains(g)))
        {
            goal.RemoveSession(task);
        }

        if (task.State == TaskState.Someday && added.Count > 0)
        {
            task.PlanForWeek(); // мысль из ленты — занятие без дня
        }

        foreach (var goal in added)
        {
            goal.AddSession(task);
        }

        if (!task.IsSession && task.State == TaskState.Planned && task.PlannedDate is null)
        {
            await placer.ReturnToSomedayAsync(task, ct); // занятие без дня без целей нигде не видно — в ленту
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new TaskGoalsUpdate(task, current.Union(wanted).ToList(), replaced);
    }
}
