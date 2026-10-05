using Planner.Shared;

namespace Planner.Client.Services;

// Копилки привычек: список со статистикой (её считает сервер), «+1 сегодня», каникулы, подсказка «📎 +1»
public partial class PlannerState
{
    /// <summary>Все копилки (и в архиве) по порядку, со статистикой на сегодня.</summary>
    public List<HabitDto> Habits { get; private set; } = [];

    /// <summary>Каникулы, новые сверху.</summary>
    public List<VacationDto> Vacations { get; private set; } = [];

    /// <summary>Копилки не в архиве — их предлагает «🫙 В копилку».</summary>
    public IEnumerable<HabitDto> ActiveHabits => Habits.Where(h => !h.IsArchived).OrderBy(h => h.SortOrder);

    public HabitDto? FindHabit(int? habitId) => habitId is { } id ? Habits.FirstOrDefault(h => h.Id == id) : null;

    /// <summary>Перечитать копилки (скрепки считаются на сервере по делам).</summary>
    public async Task RefreshHabitsAsync()
    {
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

    /// <summary>Новая копилка (ждём сервер — Id нужен сразу, например чтобы выбрать её в окне цели). null — не сохранилась.</summary>
    public async Task<HabitDto?> CreateHabitAsync(SaveHabitRequest request)
    {
        try
        {
            await CommitPendingDeleteAsync();
            var habit = await api.CreateHabitAsync(request);
            Habits.Add(habit);
            NotifyChanged();
            return habit;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return null;
        }
    }

    /// <summary>Сохранить окно копилки (и «в архив» / «из архива»).</summary>
    public async Task UpdateHabitAsync(int habitId, SaveHabitRequest request)
    {
        try
        {
            await CommitPendingDeleteAsync();
            var saved = await api.UpdateHabitAsync(habitId, request, RuDates.Today);
            var index = Habits.FindIndex(h => h.Id == habitId);
            if (index >= 0)
            {
                Habits[index] = saved;
            }
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    /// <summary>Удалить копилку: дела и цели остаются, без копилки. Спрашивает окно копилки (без «Вернуть» — счёт не восстановить).</summary>
    public async Task DeleteHabitAsync(int habitId)
    {
        Habits.RemoveAll(h => h.Id == habitId);
        foreach (var goal in Goals.Where(g => g.HabitId == habitId).ToList())
        {
            ReplaceGoal(goal with { HabitId = null });
        }

        foreach (var task in _tasks.Values.Where(t => t.HabitId == habitId).ToList())
        {
            _tasks[task.Id] = task with { HabitId = null };
        }

        if (await SaveAsync(() => api.DeleteHabitAsync(habitId)))
        {
            await RefreshHabitsAsync(); // каникулы только этой копилки удалились
        }
    }

    /// <summary>«+1 сегодня»: выполненное дело копилки в конец сегодняшнего дня и подсказка «📎 +1».</summary>
    public async Task PlusOneAsync(int habitId)
    {
        try
        {
            await CommitPendingDeleteAsync();
            var result = await api.PlusOneAsync(habitId, RuDates.Today);
            if (result.Task.PlannedDate is { } date && WeekDays.Contains(date))
            {
                _tasks[result.Task.Id] = result.Task;
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

    /// <summary>Каникулы с from по to (habitIds пусто — для всех копилок). false — не сохранились.</summary>
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
    /// Отметили (или сняли отметку с) дела копилки: перечитываем счёт; отметили — подсказка
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

    private static string ClipMessage(HabitDto habit, DateOnly? date)
    {
        var message = $"+1 в «{habit.Name}» · {habit.Stats.Total}";
        if (date is { } day && habit.Stats.Weeks.FirstOrDefault(w => w.WeekStart == RuDates.WeekStartOf(day)) is { } week)
        {
            message += $" · неделя {week.Count} из {habit.WeeklyNorm}{(week.Count >= habit.WeeklyNorm ? " ✓" : "")}";
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
