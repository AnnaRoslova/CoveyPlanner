using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Habits;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>
/// Привычка и её посчитанная статистика. Roles — роли серии по порядку ролей (сначала измерения пилы, потом обычные роли;
/// первая — группа на экране 🫙, остальные — чипы); WeeklyNorm — норма этой недели (цель недели, иначе шаблон серии); IsArchived — серия закончилась.
/// </summary>
public sealed record HabitOverview(GoalSeries Series, IReadOnlyList<Role> Roles, int WeeklyNorm, bool IsArchived, HabitStats Stats);

/// <summary>
/// Привычки — это постоянные серии целей с «🫙 копить» (отдельной сущности нет): норма недели — «раз в неделю»
/// цели той недели, скрепки — выполненные занятия целей серии. Скрепки не хранятся, а считаются одним запросом
/// (<see cref="IHabitRepository"/>). Здесь — банка (стартовое число, размер), «+1 сегодня», удаление из архива и каникулы.
/// </summary>
public class HabitService(
    IGoalSeriesRepository series,
    IGoalRepository goals,
    IRoleRepository roles,
    IHabitRepository habits,
    IVacationRepository vacations,
    GoalSeriesService seriesService,
    GoalSessions sessions,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Сегодня у пользователя (сервер работает на его компьютере).</summary>
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>
    /// Все привычки со статистикой на день <paramref name="today"/>: идущие и архив (законченные — только со скрепками
    /// или настроенной банкой; пустые законченные серии не показываются).
    /// </summary>
    public async Task<IReadOnlyList<HabitOverview>> GetOverviewAsync(DateOnly today, CancellationToken ct = default)
    {
        // Порядок ролей как на экране: измерения пилы, затем обычные роли
        var orderedRoles = (await roles.GetAllAsync(ct)).OrderBy(r => r.IsSharpenSaw ? 0 : 1).ThenBy(r => r.SortOrder).ToList();
        var clips = (await habits.GetClipsAsync(ct)).ToLookup(c => c.SeriesId, c => c.Day);
        var norms = (await habits.GetWeekNormsAsync(ct)).ToLookup(n => n.SeriesId);
        var allVacations = await vacations.GetAllAsync(ct);
        var currentWeek = Week.StartOf(today);

        var result = new List<HabitOverview>();
        foreach (var habit in await series.GetRecurringAsync(ct))
        {
            var habitRoles = orderedRoles.Where(r => habit.RoleIds.Contains(r.Id)).ToList();
            if (!habit.IsHabit || habitRoles.Count == 0)
            {
                continue;
            }

            var own = clips[habit.Id].ToList();
            if (habit.IsEnded && own.Count == 0 && !habit.HasCustomJar)
            {
                continue;
            }

            var weekNorms = norms[habit.Id].ToDictionary(n => n.WeekStart, n => n.TimesPerWeek);
            var norm = weekNorms.GetValueOrDefault(currentWeek, habit.TimesPerWeek);
            var firstWeek = weekNorms.Keys.Append(habit.FirstWeek).Min();
            var ranges = allVacations.Where(v => v.AppliesTo(habit.Id)).Select(v => v.Range).ToList();
            var stats = HabitStatsCalculator.Calculate(
                new HabitInput(firstWeek, norm, habit.JarSize, habit.StartCount, own, ranges, weekNorms), today);
            result.Add(new HabitOverview(habit, habitRoles, norm, habit.IsEnded, stats));
        }

        // По группам (порядок ролей), внутри — по порядку создания
        return result
            .OrderBy(o => orderedRoles.IndexOf(o.Roles[0]))
            .ThenBy(o => o.Series.Id)
            .ToList();
    }

    /// <summary>Одна привычка со статистикой.</summary>
    public async Task<HabitOverview> GetAsync(int id, DateOnly today, CancellationToken ct = default) =>
        (await GetOverviewAsync(today, ct)).FirstOrDefault(o => o.Series.Id == id) ?? throw new NotFoundException("Привычка", id);

    /// <summary>Настройки банки: стартовое число и размер.</summary>
    public async Task UpdateJarAsync(int id, int startCount, int jarSize, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        habit.SetJar(startCount, jarSize);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Удалить привычку — только из архива (серия закончилась). Удаляется только счёт: банка и каникулы этой привычки
    /// (общие каникулы остаются для остальных); цель, её серия и дела не трогаются.
    /// «Вернуть» — на клиенте: удаление уходит на сервер через несколько секунд.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        if (!habit.IsEnded)
        {
            throw new DomainException("Удалить можно только привычку из архива.");
        }

        foreach (var vacation in await vacations.GetAllAsync(ct))
        {
            if (!vacation.RemoveHabit(id))
            {
                vacations.Remove(vacation);
            }
        }

        habit.DeleteHabit();
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// «+1 сегодня» — выполненное занятие цели этой недели на <paramref name="today"/>: невыполненное сегодняшнее
    /// отмечается, иначе новое (или занятие без дня) встаёт в конец дня уже отмеченным. Правило «одно занятие в день» —
    /// как у цели: сегодня уже отмечено — второе не добавляется.
    /// </summary>
    public async Task<(WeeklyGoal Goal, TaskItem Session)> PlusOneAsync(int id, DateOnly today, CancellationToken ct = default)
    {
        var habit = await FindAsync(id, ct);
        if (habit.IsEnded)
        {
            throw new DomainException("Привычка в архиве.");
        }

        var weekStart = Week.StartOf(today);
        await seriesService.EnsureWeeksAsync(weekStart, ct);
        var goal = await goals.FindInSeriesAsync(id, weekStart, ct)
                   ?? throw new DomainException("На этой неделе цели нет.");

        var session = goal.ActiveSessions.FirstOrDefault(t => t.State == TaskState.Planned && t.PlannedDate == today);
        if (session is null)
        {
            if (!goal.AllowSeveralPerDay && goal.HasSessionOn(today))
            {
                throw new DomainException("Сегодня уже отмечено.");
            }

            session = goal.UnscheduledSession ?? sessions.Create(goal);
            await sessions.PlaceInDayAsync(goal, session, new DayTarget(today, int.MaxValue), ct);
        }

        session.Complete(Now);
        await unitOfWork.SaveChangesAsync(ct);
        return (goal, session);
    }

    /// <summary>Каникулы с <paramref name="from"/> по <paramref name="to"/>; habitIds пусто — для всех привычек.</summary>
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

    /// <summary>Серия-привычка: постоянная, с «🫙 копить» (роль не важна).</summary>
    private async Task<GoalSeries> FindAsync(int id, CancellationToken ct) =>
        await series.FindAsync(id, ct) is { IsRecurring: true, IsHabit: true } habit
            ? habit
            : throw new NotFoundException("Привычка", id);
}
