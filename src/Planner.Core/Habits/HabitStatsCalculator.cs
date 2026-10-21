using Planner.Core.Time;

namespace Planner.Core.Habits;

/// <summary>
/// Статистика привычки по её скрепкам (правила — docs/habits-mockup.html, блок «правила»). Чистая функция:
/// ни базы, ни часов — «сегодня» передаётся.
/// <list type="bullet">
/// <item>Неделя засчитана, если скрепок не меньше нормы. Текущая неделя «в процессе» и серию не ломает; как только норма
/// в ней выполнена — сразу входит в серию и снимает паузу (награда мгновенно).</item>
/// <item>Серия недель: первый пропуск — ⚠ пауза (серия жива, но не растёт), второй подряд — серия прервана;
/// длина — число засчитанных недель с начала серии.</item>
/// <item>Норма у каждой недели своя (правка цели прошлые недели не меняет); неделя без цели не засчитана и не пропуск.</item>
/// <item>Недели до начала привычки не считаются; первая неделя засчитывается, если норма выполнена, а если нет — это не пропуск.</item>
/// <item>Каникулы: неделя, в которую попало не меньше 4 дней каникул, не засчитывается и не пропуск — серия замирает
/// (пропуски до и после каникул — подряд). Скрепки в каникулы всё равно идут в общий счёт.</item>
/// <item>Неделя относится к месяцу своего четверга. Месяц засчитан, если внутри него серия недель не прервалась
/// (не было второго пропуска подряд — даже если первый был в прошлом месяце); месяц из одних каникул — каникулы.
/// Серия месяцев — по тому же правилу «⚠ после первого, обрыв после второго»; текущий месяц — «в процессе».</item>
/// </list>
/// </summary>
public static class HabitStatsCalculator
{
    /// <summary>Сколько последних недель показывать (лента квадратиков).</summary>
    public const int WeeksShown = 26;

    /// <summary>Сколько последних месяцев показывать (полоса месяцев).</summary>
    public const int MonthsShown = 7;

    /// <summary>Неделя — каникулы, если в каникулы попало столько её дней или больше.</summary>
    public const int VacationDaysForWeek = 4;

    public static HabitStats Calculate(HabitInput input, DateOnly today)
    {
        var currentWeek = Week.StartOf(today);
        var firstWeek = Week.StartOf(input.FirstDay);
        var counts = input.Clips.GroupBy(Week.StartOf).ToDictionary(g => g.Key, g => g.Count());
        int CountOf(DateOnly week) => counts.GetValueOrDefault(week);
        bool IsVacation(DateOnly week) =>
            Week.Days(week).Count(day => input.Vacations.Any(v => v.Contains(day))) >= VacationDaysForWeek;

        // Завершённые недели привычки — от первой до прошлой
        var finished = new List<DateOnly>();
        var weekResults = new List<PeriodResult>();
        var weekBreaks = new List<bool>(); // на этой неделе серия прервалась (второй пропуск подряд и дальше)
        var weekStreak = new StreakCounter(weekResults);
        for (var week = firstWeek; week < currentWeek; week = week.AddDays(Week.DaysInWeek))
        {
            finished.Add(week);
            var broke = false;
            if (IsVacation(week))
            {
                weekStreak.Skip(PeriodResult.Vacation);
            }
            else if (input.NormOf(week) is not { } norm)
            {
                weekStreak.Skip(PeriodResult.Neutral); // у недели нет цели — нечего выполнять
            }
            else if (CountOf(week) >= norm)
            {
                weekStreak.Done();
            }
            else if (week == firstWeek)
            {
                weekStreak.Skip(PeriodResult.Neutral); // неполная первая неделя без нормы — не пропуск
            }
            else
            {
                broke = weekStreak.Miss();
            }

            weekBreaks.Add(broke);
        }

        // Награда сразу: норма текущей недели уже выполнена — неделя в серии и пауза снята (не выполнена — «в процессе»)
        if (currentWeek >= firstWeek && !IsVacation(currentWeek) && input.NormOf(currentWeek) is { } currentNorm
            && CountOf(currentWeek) >= currentNorm)
        {
            weekStreak.CountCurrent();
        }

        // Месяцы — по четвергам недель
        var currentMonth = MonthOf(currentWeek);
        var firstMonth = MonthOf(firstWeek);
        var monthResults = new List<PeriodResult>();
        var monthStreak = new StreakCounter(monthResults);
        for (var month = firstMonth; month < currentMonth; month = month.AddMonths(1))
        {
            var inMonth = Enumerable.Range(0, finished.Count).Where(i => MonthOf(finished[i]) == month).ToList();
            if (inMonth.All(i => weekResults[i] == PeriodResult.Vacation))
            {
                monthStreak.Skip(PeriodResult.Vacation);
            }
            else if (inMonth.Any(i => weekBreaks[i]))
            {
                monthStreak.Miss();
            }
            else if (inMonth.All(i => weekResults[i] is PeriodResult.Neutral or PeriodResult.Vacation))
            {
                monthStreak.Skip(PeriodResult.Neutral);
            }
            else
            {
                monthStreak.Done();
            }
        }

        var weeks = Enumerable.Range(0, WeeksShown)
            .Select(i => currentWeek.AddDays(-Week.DaysInWeek * (WeeksShown - 1 - i)))
            .Select(week => new HabitWeek(week, CountOf(week),
                week < firstWeek ? PeriodResult.BeforeStart
                : week == currentWeek ? PeriodResult.Current
                : weekResults[finished.IndexOf(week)],
                input.NormOf(week)))
            .ToList();

        var months = Enumerable.Range(0, MonthsShown)
            .Select(i => currentMonth.AddMonths(-(MonthsShown - 1 - i)))
            .Select(month => new HabitMonth(month,
                month < firstMonth || currentWeek < firstWeek ? PeriodResult.BeforeStart
                : month == currentMonth ? PeriodResult.Current
                : monthResults[MonthsBetween(firstMonth, month)]))
            .ToList();

        // Среднее — по завершённым неделям, где норма что-то значила: без каникул и без неполной первой недели без нормы
        var counted = Enumerable.Range(0, finished.Count)
            .Where(i => weekResults[i] is not (PeriodResult.Vacation or PeriodResult.Neutral))
            .Select(i => CountOf(finished[i]))
            .ToList();
        var average = counted.Count > 0 ? counted.Average() : CountOf(currentWeek);

        var total = input.StartCount + input.Clips.Count;
        var jar = Math.Max(1, input.JarSize);
        var vacationNow = input.Vacations.Where(v => v.Contains(today)).Select(v => (DateOnly?)v.To).Max();

        return new HabitStats(
            Total: total,
            FullJars: total / jar,
            InJar: total % jar,
            ThisWeek: CountOf(currentWeek),
            WeekStreak: weekStreak.Length,
            BestWeekStreak: weekStreak.Best,
            WeekPaused: weekStreak.IsPaused,
            MonthStreak: monthStreak.Length,
            MonthPaused: monthStreak.IsPaused,
            Weeks: weeks,
            Months: months,
            AveragePerWeek: Math.Round(average, 1),
            ThisMonth: input.Clips.Count(d => d.Year == today.Year && d.Month == today.Month),
            ThisYear: input.Clips.Count(d => d.Year == today.Year),
            VacationUntil: vacationNow);
    }

    /// <summary>Месяц недели — месяц её четверга (первое число месяца).</summary>
    public static DateOnly MonthOf(DateOnly weekStart)
    {
        var thursday = Week.StartOf(weekStart).AddDays(3);
        return new DateOnly(thursday.Year, thursday.Month, 1);
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

    /// <summary>
    /// Серия «⚠ после первого пропуска, обрыв после второго подряд». Пишет итоги периодов в общий список:
    /// при обрыве пауза перед ним задним числом становится пропуском.
    /// </summary>
    private sealed class StreakCounter(List<PeriodResult> results)
    {
        private int? _pausedAt;

        /// <summary>Засчитанных периодов с начала серии.</summary>
        public int Length { get; private set; }

        /// <summary>Самая длинная серия за всю историю (дашборд, batch-ux.md BD2).</summary>
        public int Best { get; private set; }

        /// <summary>Пропусков подряд (каникулы и нейтральные периоды их не прерывают).</summary>
        public int Misses { get; private set; }

        public bool IsPaused => Misses == 1;

        public void Done()
        {
            Length++;
            Best = Math.Max(Best, Length);
            Misses = 0;
            _pausedAt = null;
            results.Add(PeriodResult.Done);
        }

        /// <summary>Текущий период уже засчитан досрочно: серия растёт, пауза снимается; в список итогов он не пишется («в процессе»).</summary>
        public void CountCurrent()
        {
            Length++;
            Best = Math.Max(Best, Length);
            Misses = 0;
            _pausedAt = null;
        }

        /// <summary>Период, который серию не трогает: каникулы, нейтральный.</summary>
        public void Skip(PeriodResult result) => results.Add(result);

        /// <summary>Пропуск. true — серия прервана (второй пропуск подряд и дальше).</summary>
        public bool Miss()
        {
            Misses++;
            if (Misses == 1)
            {
                _pausedAt = results.Count;
                results.Add(PeriodResult.Paused);
                return false;
            }

            Length = 0;
            if (_pausedAt is { } paused)
            {
                results[paused] = PeriodResult.Missed;
                _pausedAt = null;
            }

            results.Add(PeriodResult.Missed);
            return true;
        }
    }
}
