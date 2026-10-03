using Planner.Shared;

namespace Planner.Client.Services;

// Дело → занятие цели: дело бросили на строку цели в левой панели
public partial class PlannerState
{
    /// <summary>
    /// Дело бросили на строку цели — оно становится занятием этой цели (было занятием другой — переходит к этой).
    /// Цель — той же недели, что и дело (мысль из ленты станет занятием без дня). Если у цели в этот день уже есть
    /// выполненное занятие — ничего не меняется (подсветка и «уже есть в этот день»); невыполненное сервер заменит
    /// этим делом (старое — в корзину), тогда плашка «Заменено … · Вернуть».
    /// </summary>
    public async Task LinkTaskToGoalAsync(int taskId, int goalId)
    {
        if (taskId < 0 || FindTask(taskId) is not { } task || FindGoal(goalId) is not { } goal || task.GoalId == goalId)
        {
            return;
        }

        if (task.PlannedDate is { } date)
        {
            if (RuDates.WeekStartOf(date) != goal.WeekStart)
            {
                notifier.Info("занятием можно сделать только дело той же недели");
                return;
            }

            if (!goal.AllowSeveralPerDay
                && goal.Sessions.FirstOrDefault(s => s.PlannedDate == date && s.TaskId != taskId && s.IsDone) is { } taken)
            {
                RefuseDayTaken(taken.TaskId);
                return;
            }
        }

        var before = task.GoalId;
        TaskGoalResultDto? result = null;
        if (!await SaveAsync(async () => result = await api.SetTaskGoalAsync(taskId, goalId)))
        {
            return;
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
    }

    /// <summary>«Вернуть» после замены: дело — снова занятие прежней цели (или обычное), заменённые занятия — из корзины на место.</summary>
    private async Task UndoReplaceAsync(int taskId, int? goalId, IReadOnlyList<int> replaced)
    {
        await SaveAsync(async () =>
        {
            await api.SetTaskGoalAsync(taskId, goalId);
            foreach (var id in replaced)
            {
                await api.RestoreTaskAsync(id);
            }
        });
        await ReloadAsync();
    }
}
