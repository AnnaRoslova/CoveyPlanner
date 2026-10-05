using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;
using Planner.Core.Habits;

namespace Planner.Core.Services;

/// <summary>Поля окна копилки. Color = null — следующий цвет палитры.</summary>
public sealed record HabitFields(
    string Name, string? Emoji, string? Color, int WeeklyNorm, int JarSize = Habit.DefaultJarSize, int StartCount = 0);

/// <summary>Копилка и её посчитанная статистика.</summary>
public sealed record HabitOverview(Habit Habit, HabitStats Stats);

/// <summary>
/// Копилки привычек и каникулы: создать, изменить, «+1 сегодня», статистика. Скрепки не хранятся —
/// считаются по выполненным делам одним запросом (<see cref="IHabitRepository.GetClipsAsync"/>).
/// </summary>
public class HabitService(
    IHabitRepository habits,
    IVacationRepository vacations,
    ITaskRepository tasks,
    TaskPlacer placer,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Сегодня у пользователя (сервер работает на его компьютере).</summary>
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Все копилки (и в архиве) со статистикой на день <paramref name="today"/>.</summary>
    public async Task<IReadOnlyList<HabitOverview>> GetOverviewAsync(DateOnly today, CancellationToken ct = default)
    {
        var all = await habits.GetAllAsync(ct);
        var clips = (await habits.GetClipsAsync(ct)).ToLookup(c => c.HabitId, c => c.Day(clock.LocalTimeZone));
        var allVacations = await vacations.GetAllAsync(ct);
        return all.Select(h => new HabitOverview(h, Calculate(h, [.. clips[h.Id]], allVacations, today))).ToList();
    }

    /// <summary>Одна копилка со статистикой.</summary>
    public async Task<HabitOverview> GetAsync(int id, DateOnly today, CancellationToken ct = default) =>
        (await GetOverviewAsync(today, ct)).FirstOrDefault(o => o.Habit.Id == id) ?? throw new NotFoundException("Копилка", id);

    /// <summary>Новая копилка — в конец списка.</summary>
    public async Task<Habit> CreateAsync(HabitFields fields, CancellationToken ct = default)
    {
        var all = await habits.GetAllAsync(ct);
        var habit = Habit.Create(fields.Name, fields.Emoji, fields.Color ?? RolePalette.ForIndex(all.Count),
            fields.WeeklyNorm, fields.JarSize, fields.StartCount, Now);
        habit.SortOrder = all.Count == 0 ? 0 : all.Max(h => h.SortOrder) + 1;
        habits.Add(habit);
        await unitOfWork.SaveChangesAsync(ct);
        return habit;
    }

    /// <summary>Сохранить окно копилки; <paramref name="isArchived"/> = null — архив не менять.</summary>
    public async Task<Habit> UpdateAsync(int id, HabitFields fields, bool? isArchived = null, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        habit.Update(fields.Name, fields.Emoji, fields.Color ?? habit.Color, fields.WeeklyNorm, fields.JarSize, fields.StartCount);
        if (isArchived is { } archived)
        {
            habit.SetArchived(archived);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return habit;
    }

    /// <summary>
    /// Удалить копилку. Дела и цели остаются, просто без копилки (связь обнуляет база); каникулы только этой копилки
    /// удаляются, у общих она убирается из списка.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        foreach (var vacation in await vacations.GetAllAsync(ct))
        {
            if (!vacation.RemoveHabit(id))
            {
                vacations.Remove(vacation);
            }
        }

        habits.Remove(habit);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>«+1 сегодня»: выполненное дело с названием копилки — в конец дел дня <paramref name="today"/> без времени.</summary>
    public async Task<TaskItem> PlusOneAsync(int id, DateOnly today, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        if (habit.IsArchived)
        {
            throw new DomainException("Копилка в архиве.");
        }

        var task = TaskItem.CreatePlanned(habit.Name, Now);
        task.SetHabit(habit.Id);
        await placer.PlaceAsync(task, new DayTarget(today, int.MaxValue), ct);
        task.Complete(Now);
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>Каникулы с <paramref name="from"/> по <paramref name="to"/>; habitIds пусто — для всех копилок.</summary>
    public async Task<Vacation> CreateVacationAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<int> habitIds, CancellationToken ct = default)
    {
        foreach (var habitId in habitIds.Distinct())
        {
            await FindAsync(habitId, ct);
        }

        var vacation = Vacation.Create(from, to, habitIds);
        vacations.Add(vacation);
        await unitOfWork.SaveChangesAsync(ct);
        return vacation;
    }

    /// <summary>Все каникулы, новые сверху.</summary>
    public Task<List<Vacation>> GetVacationsAsync(CancellationToken ct = default) => vacations.GetAllAsync(ct);

    public async Task DeleteVacationAsync(int id, CancellationToken ct = default)
    {
        var vacation = await vacations.FindAsync(id, ct) ?? throw new NotFoundException("Каникулы", id);
        vacations.Remove(vacation);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private HabitStats Calculate(Habit habit, List<DateOnly> clips, IEnumerable<Vacation> allVacations, DateOnly today)
    {
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(habit.CreatedAt, clock.LocalTimeZone));
        var ranges = allVacations.Where(v => v.AppliesTo(habit.Id)).Select(v => v.Range).ToList();
        return HabitStatsCalculator.Calculate(
            new HabitInput(firstDay, habit.WeeklyNorm, habit.JarSize, habit.StartCount, clips, ranges), today);
    }

    private async Task<Habit> FindAsync(int id, CancellationToken ct) =>
        await habits.FindAsync(id, ct) ?? throw new NotFoundException("Копилка", id);
}

/// <summary>Привязка цели или дела к копилке.</summary>
public static class HabitLinks
{
    /// <summary>Копилка по выбору в окне: 0 — «без копилки» (null), иначе копилка должна существовать.</summary>
    public static async Task<int?> ResolveLinkAsync(this IHabitRepository habits, int habitId, CancellationToken ct = default) =>
        habitId == 0 ? null : (await habits.FindAsync(habitId, ct) ?? throw new NotFoundException("Копилка", habitId)).Id;
}
