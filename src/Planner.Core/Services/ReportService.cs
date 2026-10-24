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
/// Missed / NotNeeded — пропуски занятий серии в неделях периода (docs/specs/skips.md, O1; план — уже без «не надо», H1).
public sealed record ReportSeriesRow(
    GoalSeries Series, IReadOnlyList<Role> Roles, int Plan, int Fact, int Percent, int? WeekStreak, bool Paused, int Missed = 0, int NotNeeded = 0);

/// <summary>Разовые цели периода одной строкой (N5.4): сколько «камней», сколько выполнено; пропуски их занятий (skips.md, O1).</summary>
public sealed record ReportOneOff(int Goals, int Done, int Missed = 0, int NotNeeded = 0);

/// <summary>Роль за период (skips.md, O1): дела с этой ролью — сделано (по дню дела), не сделала, не надо; Role = null — «без роли».</summary>
public sealed record ReportRoleRow(Role? Role, int Done, int Missed, int NotNeeded);

/// <summary>Пропуск периода для списка «Причины» (O2): Goal — цель, чьим занятием было дело (null — не занятие; "" — цель удалена).</summary>
public sealed record ReportSkip(DateOnly Date, string Title, Enums.SkipKind Kind, string? Reason, IReadOnlyList<string> Roles, string? Goal);

/// <summary>«Выполненное» за период (N5.5): группа категории (null — «без категории») и дела с днём отметки в периоде.</summary>
public sealed record ReportDoneGroup(Category? Category, IReadOnlyList<DoneEntry> Entries);

/// <summary>Отчёт за период с <see cref="From"/> по <see cref="To"/> включительно; Weeks — понедельники недель, чей четверг в периоде.</summary>
public sealed record Report(
    DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Weeks, IReadOnlyList<ReportSeriesRow> Series, ReportOneOff OneOff,
    IReadOnlyList<ReportDoneGroup> Done, IReadOnlyList<ReportRoleRow>? Roles = null, IReadOnlyList<ReportSkip>? Skips = null);

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
    ITaskRepository tasks,
    ITaskSkipRepository skips,
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

        // Пропуски периода (skips.md, O): по дню дела; цель — по записи истории
        var periodSkips = await skips.GetInPeriodAsync(from, to, ct);
        var goalsById = new Dictionary<int, WeeklyGoal>();
        foreach (var week in weeks)
        {
            foreach (var goal in await goals.GetForWeekAsync(week, ct))
            {
                goalsById[goal.Id] = goal;
            }
        }

        foreach (var id in periodSkips.Select(s => s.GoalId).OfType<int>().Distinct().Where(id => !goalsById.ContainsKey(id)))
        {
            if (await goals.FindAsync(id, ct) is { } goal)
            {
                goalsById[id] = goal;
            }
        }

        int SkipsOf(Func<WeeklyGoal, bool> goalFits, Enums.SkipKind kind) => periodSkips.Count(s =>
            s.Kind == kind && s.GoalId is { } id && goalsById.TryGetValue(id, out var g) && weekSet.Contains(g.WeekStart) && goalFits(g));

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
            rows.Add(new ReportSeriesRow(s, seriesRoles, plan, fact, fact * 100 / plan, streak, paused,
                SkipsOf(g => g.SeriesId == s.Id, Enums.SkipKind.Missed), SkipsOf(g => g.SeriesId == s.Id, Enums.SkipKind.NotNeeded)));
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

        bool IsOneOff(WeeklyGoal g) => !(g.Series is { IsRecurring: true } || (g.SeriesId is { } sid && recurringIds.Contains(sid)));
        var oneOff = new ReportOneOff(oneOffGoals, oneOffDone,
            SkipsOf(IsOneOff, Enums.SkipKind.Missed), SkipsOf(IsOneOff, Enums.SkipKind.NotNeeded));

        // По ролям (O1): сделано — дела в плане с днём в периоде; пропуски — по истории
        var skippedTasks = (await tasks.FindManyAsync(periodSkips.Select(s => s.TaskId).Distinct().ToList(), ct)).ToDictionary(t => t.Id);
        var doneTasks = (await tasks.GetPlannedAsync(from, to, ct)).Where(t => t.State == Enums.TaskState.Done).ToList();
        IReadOnlyList<int> RolesOf(int taskId) => skippedTasks.TryGetValue(taskId, out var t) ? t.RoleIds : [];
        var roleRows = orderedRoles
            .Select(role => new ReportRoleRow(role,
                doneTasks.Count(t => t.RoleIds.Contains(role.Id)),
                periodSkips.Count(s => s.Kind == Enums.SkipKind.Missed && RolesOf(s.TaskId).Contains(role.Id)),
                periodSkips.Count(s => s.Kind == Enums.SkipKind.NotNeeded && RolesOf(s.TaskId).Contains(role.Id))))
            .Where(row => row.Done + row.Missed + row.NotNeeded > 0)
            .ToList();
        var noRole = new ReportRoleRow(null,
            doneTasks.Count(t => t.RoleIds.Count == 0),
            periodSkips.Count(s => s.Kind == Enums.SkipKind.Missed && RolesOf(s.TaskId).Count == 0),
            periodSkips.Count(s => s.Kind == Enums.SkipKind.NotNeeded && RolesOf(s.TaskId).Count == 0));
        if (noRole.Done + noRole.Missed + noRole.NotNeeded > 0)
        {
            roleRows.Add(noRole);
        }

        // Причины (O2): свежие сверху
        var roleNames = orderedRoles.ToDictionary(x => x.Id, x => x.Name);
        var reasons = periodSkips
            .Select(s => new ReportSkip(
                s.Date,
                skippedTasks.TryGetValue(s.TaskId, out var t) ? t.Title : "—",
                s.Kind,
                s.Reason,
                RolesOf(s.TaskId).Select(id => roleNames.GetValueOrDefault(id)).OfType<string>().ToList(),
                s.GoalId is not { } gid ? null : goalsById.TryGetValue(gid, out var g) ? g.Title : ""))
            .ToList();

        return new Report(from, to, weeks, rows, oneOff, doneGroups, roleRows, reasons);
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
