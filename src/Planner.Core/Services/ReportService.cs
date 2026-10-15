using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Habits;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>
/// Строка «Привычки и постоянные цели» (N5.3): план — сумма «раз в неделю» целей серии за недели периода, факт — скрепки
/// (выполненные занятия, и удалённых целей тоже) в этих неделях; Percent — факт от плана вниз до целого.
/// WeekStreak / Paused — 🔥 и ⚠ на конец периода (только у привычек — серий с «копить»).
/// </summary>
public sealed record ReportSeriesRow(GoalSeries Series, IReadOnlyList<Role> Roles, int Plan, int Fact, int Percent, int? WeekStreak, bool Paused);

/// <summary>Разовые цели периода одной строкой (N5.4): сколько «камней» и сколько из них выполнено.</summary>
public sealed record ReportOneOff(int Goals, int Done);

/// <summary>«Выполненное» за период (N5.5): группа категории (null — «без категории») и дела с днём отметки в периоде.</summary>
public sealed record ReportDoneGroup(Category? Category, IReadOnlyList<DoneEntry> Entries);

/// <summary>Отчёт за период с <see cref="From"/> по <see cref="To"/> включительно; Weeks — понедельники недель, чей четверг в периоде.</summary>
public sealed record Report(
    DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Weeks, IReadOnlyList<ReportSeriesRow> Series, ReportOneOff OneOff,
    IReadOnlyList<ReportDoneGroup> Done);

/// <summary>
/// Отчёт за период (docs/specs/next-batch.md, N5): только чтение по уже существующим данным — нормы недель и скрепки
/// (<see cref="IHabitRepository"/>), цели недель, «Выполненное». Ничего не записывает и цели будущих недель не создаёт (N5.7).
/// </summary>
public class ReportService(
    IGoalSeriesRepository series,
    IGoalRepository goals,
    IRoleRepository roles,
    IHabitRepository habits,
    IVacationRepository vacations,
    DoneArchiveService done,
    TimeProvider clock)
{
    /// <summary>Самый длинный период отчёта — 10 лет (защита от опечатки в годе).</summary>
    public const int MaxDays = 3660;

    public async Task<Report> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        if (to.DayNumber - from.DayNumber > MaxDays)
        {
            throw new Errors.DomainException("Слишком длинный период — не больше 10 лет.");
        }

        var weeks = WeeksOf(from, to);
        var weekSet = weeks.ToHashSet();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var orderedRoles = (await roles.GetAllAsync(ct)).OrderBy(r => r.IsSharpenSaw ? 0 : 1).ThenBy(r => r.SortOrder).ToList();
        var clips = (await habits.GetClipsAsync(ct)).ToLookup(c => c.SeriesId, c => c.Day);
        var norms = (await habits.GetWeekNormsAsync(ct)).ToLookup(n => n.SeriesId);
        var allVacations = await vacations.GetAllAsync(ct);

        var rows = new List<ReportSeriesRow>();
        foreach (var s in await series.GetRecurringAsync(ct))
        {
            var weekNorms = norms[s.Id].ToDictionary(n => n.WeekStart, n => n.TimesPerWeek);
            var plan = weekNorms.Where(n => weekSet.Contains(n.Key)).Sum(n => n.Value);
            if (plan == 0)
            {
                continue; // в периоде у серии не было целей — нечего показывать (как недели без нормы у привычек)
            }

            var own = clips[s.Id].ToList();
            var fact = own.Count(day => weekSet.Contains(Week.StartOf(day)));
            int? streak = null;
            var paused = false;
            if (s.IsHabit)
            {
                var end = to < today ? to : today;
                var firstWeek = weekNorms.Keys.Append(s.FirstWeek).Min();
                var ranges = allVacations.Where(v => v.AppliesTo(s.Id)).Select(v => v.Range).ToList();
                var stats = HabitStatsCalculator.Calculate(
                    new HabitInput(firstWeek, weekNorms.GetValueOrDefault(Week.StartOf(end), s.TimesPerWeek), s.JarSize, s.StartCount,
                        own, ranges, weekNorms), end);
                streak = stats.WeekStreak;
                paused = stats.WeekPaused;
            }

            var seriesRoles = orderedRoles.Where(r => s.RoleIds.Contains(r.Id)).ToList();
            rows.Add(new ReportSeriesRow(s, seriesRoles, plan, fact, fact * 100 / plan, streak, paused));
        }

        // Как экран 🫙: по первой роли (сначала измерения пилы), внутри — по порядку создания
        rows = rows
            .OrderBy(r => r.Roles.Count == 0 ? int.MaxValue : orderedRoles.IndexOf(r.Roles[0]))
            .ThenBy(r => r.Series.Id)
            .ToList();

        // Разовые цели — цели недель периода, не из постоянной серии
        var recurringIds = rows.Select(r => r.Series.Id).ToHashSet();
        var oneOffGoals = 0;
        var oneOffDone = 0;
        foreach (var week in weeks)
        {
            foreach (var goal in await goals.GetForWeekAsync(week, ct))
            {
                if (goal.Series is { IsRecurring: true } || (goal.SeriesId is { } id && recurringIds.Contains(id)))
                {
                    continue;
                }

                oneOffGoals++;
                if (goal.IsDone)
                {
                    oneOffDone++;
                }
            }
        }

        var doneGroups = (await done.GetAsync(ct))
            .Select(g => new ReportDoneGroup(g.Category, g.Entries.Where(e => e.Date >= from && e.Date <= to).ToList()))
            .Where(g => g.Entries.Count > 0)
            .ToList();

        return new Report(from, to, weeks, rows, new ReportOneOff(oneOffGoals, oneOffDone), doneGroups);
    }

    /// <summary>Недели периода (N5.3): неделя входит, если её четверг попал в период, — «месяц» не делит недели пополам.</summary>
    public static IReadOnlyList<DateOnly> WeeksOf(DateOnly from, DateOnly to)
    {
        var weeks = new List<DateOnly>();
        for (var monday = Week.StartOf(from); monday <= to; monday = monday.AddDays(7))
        {
            var thursday = monday.AddDays(3);
            if (thursday >= from && thursday <= to)
            {
                weeks.Add(monday);
            }
        }

        return weeks;
    }
}
