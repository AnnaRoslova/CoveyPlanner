using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Habits;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>Привычка на тепловой карте (BD2): скрепки периода по дням (день повторяется, сколько скрепок), 🔥 сейчас и лучшая серия.</summary>
public sealed record DashboardHabit(HabitOverview Habit, IReadOnlyList<DateOnly> Clips);

/// <summary>Сколько выполненных занятий (BD3) или дел (BD4) пришлось на роль; Role = null — «без роли».</summary>
public sealed record DashboardRoleCount(Role? Role, int Count);

/// <summary>II квадрант недели (BD5): выполненных дел с квадрантом II, с отмеченным квадрантом и без квадранта.</summary>
public sealed record DashboardQuadrantWeek(DateOnly WeekStart, int Second, int Marked, int Unmarked);

/// <summary>Давно лежащие мысли (BD6): сколько старше срока и самые старые.</summary>
public sealed record DashboardStale(int Months, int Count, IReadOnlyList<TaskItem> Oldest);

/// <summary>Дашборд за период: всё одним ответом (BD8).</summary>
public sealed record Dashboard(
    DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Weeks,
    IReadOnlyList<DashboardHabit> Habits, IReadOnlyList<Vacation> Vacations,
    IReadOnlyList<DashboardRoleCount> Saw, IReadOnlyList<DashboardRoleCount> Roles,
    IReadOnlyList<DashboardQuadrantWeek> Quadrants, DashboardStale Stale);

/// <summary>
/// Дашборд во вкладке «Отчёт» (docs/specs/batch-ux.md, BD): только чтение. Привычки и серии — как на экране 🫙
/// (<see cref="HabitService"/>), выполненное — по дню отметки (как «Выполненное»), недели периода — по четвергу (<see cref="ReportService.WeeksOf"/>).
/// </summary>
public class DashboardService(
    ITaskRepository tasks,
    IRoleRepository roles,
    IHabitRepository habits,
    IVacationRepository vacations,
    HabitService habitService,
    TimeProvider clock)
{
    /// <summary>Срок «давно лежат» по умолчанию, месяцев (BS; потом — из настроек).</summary>
    public const int DefaultStaleMonths = 3;

    /// <summary>Сколько самых старых мыслей показывать (BD6).</summary>
    public const int StaleTop = 5;

    public async Task<Dashboard> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        if (to.DayNumber - from.DayNumber > ReportService.MaxDays)
        {
            throw new Errors.DomainException("Слишком длинный период — не больше 10 лет.");
        }

        var weeks = ReportService.WeeksOf(from, to);

        // Тепловая карта — по дням недель периода (неделя целиком, как столбец)
        var firstDay = weeks.Count > 0 ? weeks[0] : Week.StartOf(from);
        var lastDay = weeks.Count > 0 ? weeks[^1].AddDays(Week.DaysInWeek - 1) : Week.StartOf(from).AddDays(Week.DaysInWeek - 1);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var clips = (await habits.GetClipsAsync(ct)).ToLookup(c => c.SeriesId, c => c.Day);
        var dashboardHabits = (await habitService.GetOverviewAsync(today, ct))
            .Select(h => new DashboardHabit(h, clips[h.Series.Id].Where(d => d >= firstDay && d <= lastDay).Order().ToList()))
            .ToList();
        var periodVacations = (await vacations.GetAllAsync(ct)).Where(v => v.From <= lastDay && v.To >= firstDay).ToList();

        // Выполненное за период — по дню отметки у пользователя
        var orderedRoles = (await roles.GetAllAsync(ct)).OrderBy(r => r.IsSharpenSaw ? 0 : 1).ThenBy(r => r.SortOrder).ToList();
        var completed = (await tasks.GetCompletedAsync(ToUtc(from), ToUtc(to.AddDays(1)), ct))
            .Select(t => (Task: t, Day: LocalDate(t.CompletedAt!.Value)))
            .Where(x => x.Day >= from && x.Day <= to)
            .ToList();

        // BD3: занятие — в каждом измерении своей цели (роли занятия = роли цели)
        var sessions = completed.Where(x => x.Task.IsSession).Select(x => x.Task).ToList();
        var saw = orderedRoles.Where(r => r.IsSharpenSaw)
            .Select(r => new DashboardRoleCount(r, sessions.Count(t => t.RoleIds.Contains(r.Id))))
            .ToList();

        // BD4: дело — в каждой своей роли; измерения пилы сверху (порядок ролей), «без роли» — внизу
        var roleCounts = orderedRoles
            .Select(r => new DashboardRoleCount(r, completed.Count(x => x.Task.RoleIds.Contains(r.Id))))
            .Where(c => c.Count > 0)
            .ToList();
        var withoutRole = completed.Count(x => x.Task.RoleIds.Count == 0);
        if (withoutRole > 0)
        {
            roleCounts.Add(new DashboardRoleCount(null, withoutRole));
        }

        // BD5: доля II среди дел с отмеченным квадрантом, по неделям периода
        var quadrants = weeks
            .Select(week =>
            {
                var ofWeek = completed.Where(x => Week.StartOf(x.Day) == week).Select(x => x.Task).ToList();
                return new DashboardQuadrantWeek(
                    week,
                    ofWeek.Count(t => t.Quadrant == Quadrant.NotUrgentImportant),
                    ofWeek.Count(t => t.Quadrant is not null),
                    ofWeek.Count(t => t.Quadrant is null));
            })
            .ToList();

        return new Dashboard(from, to, weeks, dashboardHabits, periodVacations, saw, roleCounts, quadrants,
            await GetStaleAsync(DefaultStaleMonths, ct));
    }

    /// <summary>BD6: мысли ленты (не проекты), записанные раньше, чем <paramref name="months"/> месяцев назад; самые старые сверху.</summary>
    private async Task<DashboardStale> GetStaleAsync(int months, CancellationToken ct)
    {
        var border = clock.GetUtcNow().UtcDateTime.AddMonths(-months);
        var stale = (await tasks.GetSomedayAsync(ct))
            .Where(t => !t.IsProject && t.CreatedAt < border)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToList();
        return new DashboardStale(months, stale.Count, stale.Take(StaleTop).ToList());
    }

    private DateTime ToUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), clock.LocalTimeZone);

    private DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone));
}
