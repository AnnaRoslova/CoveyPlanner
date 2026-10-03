using Planner.Shared;

namespace Planner.Client.Services;

// Одно дело — несколько целей: «Прогулка в парке» засчитывается и в «10к шагов», и в «Природу»
public partial class PlannerState
{
    /// <summary>
    /// Дело бросили на строку цели в левой панели — засчитать и в эту цель.
    /// Цель — той же недели, что и дело (мысль из ленты станет занятием без дня).
    /// </summary>
    public async Task LinkTaskToGoalAsync(int taskId, int goalId)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || FindGoal(goalId) is not { } goal
            || task.GoalIds.Contains(goalId))
        {
            return;
        }

        if (task.PlannedDate is { } date && RuDates.WeekStartOf(date) != goal.WeekStart)
        {
            notifier.Info("засчитать можно только в цель той же недели");
            return;
        }

        await SetTaskGoalsAsync(taskId, [.. task.GoalIds, goalId]);
    }

    /// <summary>
    /// В какие цели засчитывается дело (окно дела или бросок на цель). Если у новой цели в этот день уже есть
    /// выполненное занятие — связь не создаётся (подсветка и «уже есть в этот день»); невыполненное сервер заменит этим
    /// делом (старое — в корзину), тогда плашка «Заменено … · Вернуть». false — ничего не поменялось.
    /// </summary>
    public async Task<bool> SetTaskGoalsAsync(int taskId, IReadOnlyList<int> goalIds)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task)
        {
            return false;
        }

        var before = task.GoalIds;
        if (before.Count == goalIds.Count && before.All(goalIds.Contains))
        {
            return false;
        }

        if (task.PlannedDate is { } date)
        {
            var taken = goalIds.Except(before).Select(FindGoal).OfType<WeeklyGoalDto>()
                .Where(g => !g.AllowSeveralPerDay)
                .SelectMany(g => g.Sessions)
                .FirstOrDefault(s => s.PlannedDate == date && s.TaskId != taskId && s.IsDone);
            if (taken is not null)
            {
                RefuseDayTaken(taken.TaskId);
                return false;
            }
        }

        TaskGoalsResultDto? result = null;
        if (!await SaveAsync(async () => result = await api.SetTaskGoalsAsync(taskId, goalIds)))
        {
            return false;
        }

        // Названия заменённых — пока они ещё на экране
        var replaced = result!.ReplacedTaskIds;
        var title = replaced.Count == 1 && FindTask(replaced[0]) is { } old ? old.Title : null;
        await ReloadAsync(); // прогресс целей, роли дела, лента — берём с сервера

        if (replaced.Count > 0)
        {
            var message = title is not null ? $"Заменено «{title}»" : $"Заменено занятий: {replaced.Count}";
            notifier.WithAction(message, "Вернуть", () => UndoReplaceAsync(taskId, before, replaced), UndoWindow);
        }

        return true;
    }

    /// <summary>«Вернуть» после замены: дело — с прежними целями, заменённые занятия — из корзины на место.</summary>
    private async Task UndoReplaceAsync(int taskId, IReadOnlyList<int> goalIds, IReadOnlyList<int> replaced)
    {
        await SaveAsync(async () =>
        {
            await api.SetTaskGoalsAsync(taskId, goalIds);
            foreach (var id in replaced)
            {
                await api.RestoreTaskAsync(id);
            }
        });
        await ReloadAsync();
    }
}
