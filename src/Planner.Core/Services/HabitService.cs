using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Habits;

namespace Planner.Core.Services;

/// <summary>
/// Поля окна копилки. Color = null — следующий цвет палитры. Kind — привычка или коллекция; YearGoal — цель коллекции на год;
/// CategoryIds — категории ленты коллекции (null — не менять).
/// </summary>
public sealed record HabitFields(
    string Name,
    string? Emoji,
    string? Color,
    int WeeklyNorm,
    int JarSize = Habit.DefaultJarSize,
    int StartCount = 0,
    HabitKind Kind = HabitKind.Habit,
    int? YearGoal = null,
    IReadOnlyList<int>? CategoryIds = null);

/// <summary>Копилка и её посчитанная статистика: серии (для привычки) и полка (для коллекции) — считаются обе, вид решает, что показать.</summary>
public sealed record HabitOverview(Habit Habit, HabitStats Stats, CollectionStats Collection);

/// <summary>
/// Копилки (привычки и коллекции) и каникулы: создать, изменить, «+1 сегодня», статистика. Скрепки не хранятся —
/// считаются по выполненным делам одним запросом (<see cref="IHabitRepository.GetClipsAsync"/>); чья скрепка — <see cref="HabitAttribution"/>.
/// </summary>
public class HabitService(
    IHabitRepository habits,
    IVacationRepository vacations,
    ITaskRepository tasks,
    ICategoryRepository categories,
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
        var collectionOfCategory = CollectionOfCategory(all);
        var zone = clock.LocalTimeZone;
        var clips = (await habits.GetClipsAsync([.. collectionOfCategory.Keys], ct))
            .Select(c => (Clip: c, HabitId: HabitAttribution.HabitOf(c, collectionOfCategory)))
            .Where(c => c.HabitId is not null)
            .ToLookup(c => c.HabitId!.Value, c => c.Clip);
        var allVacations = await vacations.GetAllAsync(ct);

        return all.Select(h =>
        {
            var own = clips[h.Id].ToList();
            var items = own.Select(c => new CollectionItem(c.TaskId, c.Title, c.CompletedOn(zone), c.StepCount, c.FirstStepOn(zone))).ToList();
            return new HabitOverview(h,
                Calculate(h, [.. own.Select(c => c.Day(zone))], allVacations, today),
                CollectionStatsCalculator.Calculate(items, h.StartCount, h.YearGoal, today));
        }).ToList();
    }

    /// <summary>Категория ленты → коллекция (только копилки вида «коллекция»; у категории — не больше одной).</summary>
    public static IReadOnlyDictionary<int, int> CollectionOfCategory(IEnumerable<Habit> all) =>
        all.Where(h => h.IsCollection)
            .SelectMany(h => h.CategoryIds.Select(categoryId => (categoryId, h.Id)))
            .GroupBy(x => x.categoryId)
            .ToDictionary(g => g.Key, g => g.First().Id);

    /// <summary>Одна копилка со статистикой.</summary>
    public async Task<HabitOverview> GetAsync(int id, DateOnly today, CancellationToken ct = default) =>
        (await GetOverviewAsync(today, ct)).FirstOrDefault(o => o.Habit.Id == id) ?? throw new NotFoundException("Копилка", id);

    /// <summary>Новая копилка — в конец списка.</summary>
    public async Task<Habit> CreateAsync(HabitFields fields, CancellationToken ct = default)
    {
        var all = await habits.GetAllAsync(ct);
        var habit = Habit.Create(fields.Name, fields.Emoji, fields.Color ?? RolePalette.ForIndex(all.Count),
            fields.WeeklyNorm, fields.JarSize, fields.StartCount, Now);
        await ApplyCollectionFieldsAsync(habit, fields, all, ct);
        habit.SortOrder = all.Count == 0 ? 0 : all.Max(h => h.SortOrder) + 1;
        habits.Add(habit);
        await unitOfWork.SaveChangesAsync(ct);
        return habit;
    }

    /// <summary>Сохранить окно копилки; <paramref name="isArchived"/> = null — архив не менять. Смена вида историю не трогает.</summary>
    public async Task<Habit> UpdateAsync(int id, HabitFields fields, bool? isArchived = null, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        habit.Update(fields.Name, fields.Emoji, fields.Color ?? habit.Color, fields.WeeklyNorm, fields.JarSize, fields.StartCount);
        await ApplyCollectionFieldsAsync(habit, fields, await habits.GetAllAsync(ct), ct);
        if (isArchived is { } archived)
        {
            habit.SetArchived(archived);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return habit;
    }

    /// <summary>
    /// Вид, цель года и категории ленты. Категория — не больше чем у одной копилки: выбранная здесь уходит от прежней.
    /// </summary>
    private async Task ApplyCollectionFieldsAsync(Habit habit, HabitFields fields, IEnumerable<Habit> all, CancellationToken ct)
    {
        habit.SetKind(fields.Kind);
        habit.SetYearGoal(fields.YearGoal);
        if (fields.CategoryIds is not { } categoryIds)
        {
            return;
        }

        foreach (var categoryId in categoryIds.Distinct())
        {
            if (await categories.FindAsync(categoryId, ct) is null)
            {
                throw new NotFoundException("Категория", categoryId);
            }

            foreach (var other in all.Where(h => h != habit))
            {
                other.RemoveCategory(categoryId);
            }
        }

        habit.SetCategories(categoryIds);
    }

    /// <summary>
    /// Удалить копилку — только из архива (активную сначала отправляют в архив). Дела и цели остаются, просто без копилки
    /// (связь обнуляет база); каникулы только этой копилки удаляются, у общих она убирается из списка.
    /// «Вернуть» — на клиенте: удаление уходит на сервер через несколько секунд.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        if (!habit.IsArchived)
        {
            throw new DomainException("Удалить можно только копилку из архива.");
        }
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

        if (habit.IsCollection)
        {
            throw new DomainException("У коллекции нет «+1 сегодня» — скрепка в ней только выполненное дело.");
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
