using Planner.Shared;

namespace Planner.Client.Services;

// Привычки — постоянные цели «Затачивания пилы» (id привычки — id серии цели): список со статистикой (её считает сервер),
// настройки банки, «+1 сегодня», каникулы, подсказка «📎 +1»
public partial class PlannerState
{
    /// <summary>Все привычки (и в архиве) по измерениям пилы, со статистикой на сегодня.</summary>
    public List<HabitDto> Habits { get; private set; } = [];

    /// <summary>Каникулы, новые сверху.</summary>
    public List<VacationDto> Vacations { get; private set; } = [];

    public HabitDto? FindHabit(int? habitId) => habitId is { } id ? Habits.FirstOrDefault(h => h.Id == id) : null;

    /// <summary>Привычка цели — её серия, если та привычка; null — цель не привычка.</summary>
    public HabitDto? HabitOfGoal(WeeklyGoalDto? goal) => FindHabit(goal?.SeriesId);

    /// <summary>Перечитать привычки (скрепки считаются на сервере по занятиям).</summary>
    public async Task RefreshHabitsAsync()
    {
        // Отложенное удаление — сначала на сервер, иначе перечитанное вернуло бы удалённое на экран (как в ReloadAsync)
        await CommitPendingDeleteAsync(reload: false);
        try
        {
            ApplyHabits(await api.GetHabitsAsync(RuDates.Today));
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    private void ApplyHabits(HabitsDto habits)
    {
        Habits = [.. habits.Habits];
        Vacations = [.. habits.Vacations];
    }

    /// <summary>Настройки банки: стартовое число и размер.</summary>
    public async Task UpdateHabitAsync(int habitId, int startCount, int jarSize)
    {
        try
        {
            await CommitPendingDeleteAsync();
            ReplaceHabit(await api.UpdateHabitAsync(habitId, new UpdateHabitRequest(startCount, jarSize), RuDates.Today));
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    /// <summary>
    /// Удалить привычку из архива — с плашкой «Удалено «…» · Вернуть»: на экране сразу, на сервер — через 6 секунд
    /// (см. <see cref="DeleteWithUndoAsync"/>). Удаляется только счёт (банка, каникулы этой привычки) — цель и дела остаются.
    /// </summary>
    public async Task DeleteHabitAsync(int habitId)
    {
        if (FindHabit(habitId) is not { IsArchived: true } habit)
        {
            return;
        }

        await DeleteWithUndoAsync(Deleted(habit.Name), () => RemoveHabitLocally(habitId),
            () => api.DeleteHabitAsync(habitId), reloadAfter: true);
    }

    /// <summary>Как удаление привычки выглядит сразу (так же решает сервер).</summary>
    private void RemoveHabitLocally(int habitId)
    {
        Habits.RemoveAll(h => h.Id == habitId);
        Vacations = Vacations
            .Where(v => !v.HabitIds.SequenceEqual([habitId]))
            .Select(v => v with { HabitIds = [.. v.HabitIds.Where(id => id != habitId)] })
            .ToList();
    }

    /// <summary>«+1 сегодня»: занятие цели на сегодня отмечено (или добавлено отмеченным) и подсказка «📎 +1».</summary>
    public async Task PlusOneAsync(int habitId)
    {
        // Сегодня уже отмечено, а невыполненного сегодняшнего нет (так же откажет сервер — как у галочки цели)
        var today = RuDates.Today;
        if (Goals.FirstOrDefault(g => g.SeriesId == habitId && g.WeekStart == RuDates.WeekStartOf(today)) is { AllowSeveralPerDay: false } goal
            && goal.Sessions.Any(s => s.PlannedDate == today) && !goal.Sessions.Any(s => s.PlannedDate == today && !s.IsDone))
        {
            notifier.Info("сегодня уже отмечено");
            return;
        }

        try
        {
            await CommitPendingDeleteAsync();
            var result = await api.PlusOneAsync(habitId, RuDates.Today);
            if (result.Task.PlannedDate is { } date && WeekDays.Contains(date))
            {
                _tasks[result.Task.Id] = result.Task;
            }

            if (result.Goal.WeekStart == WeekStart)
            {
                ReplaceGoal(result.Goal);
            }

            ReplaceHabit(result.Habit);
            notifier.Clip(ClipMessage(result.Habit, result.Task.PlannedDate));
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    /// <summary>Каникулы с from по to (habitIds пусто — для всех привычек). false — не сохранились.</summary>
    public async Task<bool> CreateVacationAsync(DateOnly from, DateOnly to, IReadOnlyList<int> habitIds)
    {
        try
        {
            await CommitPendingDeleteAsync();
            await api.CreateVacationAsync(new CreateVacationRequest(from, to, habitIds));
            await RefreshHabitsAsync(); // недели каникул поменяли серии
            return true;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return false;
        }
    }

    public async Task DeleteVacationAsync(int vacationId)
    {
        Vacations.RemoveAll(v => v.Id == vacationId);
        if (await SaveAsync(() => api.DeleteVacationAsync(vacationId)))
        {
            await RefreshHabitsAsync();
        }
    }

    /// <summary>
    /// Отметили (или сняли отметку с) занятие цели-привычки: перечитываем счёт; отметили — подсказка
    /// «📎 +1 в «Тренировки» · 103 · неделя 3 из 3 ✓» (неделя — та, где стоит дело).
    /// </summary>
    private async Task AfterHabitTaskToggledAsync(int habitId, bool isDone, DateOnly? date)
    {
        await RefreshHabitsAsync();
        if (isDone && FindHabit(habitId) is { } habit)
        {
            notifier.Clip(ClipMessage(habit, date));
        }
    }

    /// <summary>
    /// Цель могла стать привычкой, перестать ею быть или уйти в архив — перечитываем, если у неё есть роль пилы
    /// (<paramref name="roleIds"/> — роли после изменения) или она уже была привычкой (<paramref name="before"/>).
    /// </summary>
    private async Task RefreshHabitsIfSawAsync(IEnumerable<int> roleIds, WeeklyGoalDto? before = null)
    {
        if (HabitOfGoal(before) is not null || roleIds.Any(id => FindRole(id)?.IsSharpenSaw == true))
        {
            await RefreshHabitsAsync();
        }
    }

    /// <summary>«+1 в «Тренировки» · 103 · неделя 3 из 3 ✓» — норма той недели, где стоит дело.</summary>
    private static string ClipMessage(HabitDto habit, DateOnly? date)
    {
        var message = $"+1 в «{habit.Name}» · {habit.Stats.Total}";
        if (date is { } day && habit.Stats.Weeks.FirstOrDefault(w => w.WeekStart == RuDates.WeekStartOf(day)) is { } week
            && (week.Norm ?? habit.WeeklyNorm) is var norm)
        {
            message += $" · неделя {week.Count} из {norm}{(week.Count >= norm ? " ✓" : "")}";
        }

        return message;
    }

    private void ReplaceHabit(HabitDto habit)
    {
        var index = Habits.FindIndex(h => h.Id == habit.Id);
        if (index >= 0)
        {
            Habits[index] = habit;
        }
        else
        {
            Habits.Add(habit);
        }
    }
}
