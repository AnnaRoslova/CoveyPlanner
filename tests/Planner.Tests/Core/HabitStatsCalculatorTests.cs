using Planner.Core.Habits;

namespace Planner.Tests.Core;

/// <summary>Правила привычек (docs/habits-mockup.html, блок «правила»): скрепки, недели, серии, месяцы, каникулы.</summary>
public class HabitStatsCalculatorTests
{
    // Сегодня — среда 7 октября 2026; текущая неделя — с понедельника 5 октября (четверг 8-го — октябрь)
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly DateOnly ThisWeek = new(2026, 10, 5);

    /// <summary>Понедельник недели со сдвигом от текущей (−1 — прошлая).</summary>
    private static DateOnly W(int offset) => ThisWeek.AddDays(7 * offset);

    /// <summary>
    /// Скрепки по неделям: каждая цифра — сколько скрепок за неделю, последняя — текущая неделя; «|» и пробелы —
    /// просто для чтения (границы месяцев). Копилка создана в понедельник первой недели (или в <paramref name="firstDay"/>).
    /// </summary>
    private static HabitStats Run(string weeks, int norm = 3, DateOnly? firstDay = null, int startCount = 0, int jarSize = 30,
        DateRange[]? vacations = null, DateOnly? today = null, IEnumerable<DateOnly>? extraClips = null,
        int[]? weekNorms = null)
    {
        var counts = weeks.Where(char.IsDigit).Select(c => c - '0').ToList();
        var first = W(-(counts.Count - 1));
        var clips = new List<DateOnly>();
        for (var i = 0; i < counts.Count; i++)
        {
            for (var n = 0; n < counts[i]; n++)
            {
                clips.Add(first.AddDays(7 * i + n % 7)); // с понедельника по дням недели
            }
        }

        clips.AddRange(extraClips ?? []);
        // Норма по неделям (как недели в weeks; 0 — у недели нет цели)
        var norms = weekNorms?.Select((n, i) => (Week: first.AddDays(7 * i), Norm: n)).Where(x => x.Norm > 0)
            .ToDictionary(x => x.Week, x => x.Norm);
        var input = new HabitInput(firstDay ?? first, norm, jarSize, startCount, clips, vacations ?? [], norms);
        return HabitStatsCalculator.Calculate(input, today ?? Today);
    }

    /// <summary>Последние недели буквами: _ до копилки, D засчитана, N нейтральная, P пауза, M пропуск, V каникулы, C текущая.</summary>
    private static string Marks(HabitStats stats, int count) =>
        new([.. stats.Weeks.TakeLast(count).Select(w => Mark(w.Result))]);

    private static string MonthMarks(HabitStats stats, int count) =>
        new([.. stats.Months.TakeLast(count).Select(m => Mark(m.Result))]);

    private static char Mark(PeriodResult result) => result switch
    {
        PeriodResult.BeforeStart => '_',
        PeriodResult.Done => 'D',
        PeriodResult.Neutral => 'N',
        PeriodResult.Paused => 'P',
        PeriodResult.Missed => 'M',
        PeriodResult.Vacation => 'V',
        PeriodResult.Current => 'C',
        _ => '?',
    };

    // ---------- Скрепки и банки ----------

    [Fact]
    public void Total_IsStartCountPlusClips_JarsSplitByJarSize()
    {
        var stats = Run("3333 3333 3", startCount: 9); // 27 скрепок + 9 стартовых

        Assert.Equal(36, stats.Total);
        Assert.Equal(1, stats.FullJars);
        Assert.Equal(6, stats.InJar);
    }

    [Fact]
    public void ExactlyFullJar_GoesToShelf_CurrentJarEmpty()
    {
        var stats = Run("0", startCount: 60, jarSize: 30);

        Assert.Equal((60, 2, 0), (stats.Total, stats.FullJars, stats.InJar));
    }

    [Fact]
    public void ClipsBeforeHabit_CountInTotal_ButWeeksBeforeStartDoNotCount()
    {
        // Дела, выполненные до создания копилки (цель привязали позже), — в общем счёте, но не в серии
        var stats = Run("3330 3", firstDay: W(-1));

        Assert.Equal(12, stats.Total);
        Assert.Equal("___NC", Marks(stats, 5)); // первая неделя копилки без нормы — не пропуск
        Assert.Equal(1, stats.WeekStreak); // текущая (вторая) неделя с нормой — уже в серии
        Assert.False(stats.WeekPaused);
    }

    // ---------- Неделя ----------

    [Fact]
    public void Week_DoneWhenNormReached_ExtraClipsOnlyAddToTotal()
    {
        var stats = Run("3 5 2 3");

        Assert.Equal("DDPC", Marks(stats, 4));
        Assert.Equal(13, stats.Total);
        Assert.Equal([3, 5, 2, 3], stats.Weeks.TakeLast(4).Select(w => w.Count));
    }

    [Fact]
    public void CurrentWeek_IsInProgress_NeverBreaksStreak()
    {
        var stats = Run("333 0");

        Assert.Equal("DDDC", Marks(stats, 4));
        Assert.Equal(3, stats.WeekStreak);
        Assert.False(stats.WeekPaused);
        Assert.Equal(0, stats.ThisWeek);
    }

    [Fact]
    public void CurrentWeek_NormMet_CountsImmediately_StillInProgress()
    {
        var stats = Run("333 4");

        Assert.Equal(4, stats.WeekStreak); // награда сразу, не дожидаясь воскресенья
        Assert.Equal(4, stats.ThisWeek);
        Assert.Equal(PeriodResult.Current, stats.Weeks[^1].Result);
    }

    [Fact]
    public void CurrentWeek_NormMet_ClearsPauseImmediately()
    {
        var stats = Run("33 0 3");

        Assert.Equal("DDPC", Marks(stats, 4)); // прошлая неделя — по-прежнему пауза в ленте
        Assert.Equal(3, stats.WeekStreak);
        Assert.False(stats.WeekPaused);
    }

    [Fact]
    public void CurrentWeek_NormNotYetMet_PauseStays()
    {
        var stats = Run("33 0 2");

        Assert.Equal(2, stats.WeekStreak);
        Assert.True(stats.WeekPaused);
    }

    [Fact]
    public void CurrentWeek_NormMet_AfterBreak_StartsNewStreak()
    {
        var stats = Run("33 00 3");

        Assert.Equal("DDMMC", Marks(stats, 5));
        Assert.Equal(1, stats.WeekStreak);
        Assert.False(stats.WeekPaused);
    }

    [Fact]
    public void CurrentWeek_Vacation_NotCountedEvenWithNorm()
    {
        var stats = Run("33 0 3", vacations: [new(ThisWeek, ThisWeek.AddDays(6))]);

        Assert.Equal(2, stats.WeekStreak);
        Assert.True(stats.WeekPaused);
    }

    [Fact]
    public void NewHabit_FirstWeekNormMet_StreakOneAtOnce()
    {
        var stats = Run("3");

        Assert.Equal(1, stats.WeekStreak);
    }

    [Fact]
    public void CurrentWeekDone_MonthStillCountedOnlyAfterItEnds()
    {
        var stats = Run("3333|3 3");

        Assert.Equal("DC", MonthMarks(stats, 2));
        Assert.Equal(1, stats.MonthStreak);
    }

    [Fact]
    public void Sunday_StillCurrentWeek_MondayClosesIt()
    {
        var sunday = Run("33 0", today: ThisWeek.AddDays(6));
        Assert.Equal("DDC", Marks(sunday, 3));

        // В понедельник прошлая неделя закрыта: 0 скрепок — первый пропуск
        var monday = Run("33 0", today: ThisWeek.AddDays(7));
        Assert.Equal("DDPC", Marks(monday, 4));
        Assert.True(monday.WeekPaused);
    }

    [Fact]
    public void Weeks_Last26_EndWithCurrent()
    {
        var stats = Run("3");

        Assert.Equal(HabitStatsCalculator.WeeksShown, stats.Weeks.Count);
        Assert.Equal(ThisWeek, stats.Weeks[^1].WeekStart);
        Assert.Equal(W(-25), stats.Weeks[0].WeekStart);
        Assert.All(stats.Weeks.SkipLast(1), w => Assert.Equal(PeriodResult.BeforeStart, w.Result));
    }

    // ---------- Первая неделя ----------

    [Fact]
    public void FirstWeek_NormMet_Counts()
    {
        var stats = Run("3 0");

        Assert.Equal("DC", Marks(stats, 2));
        Assert.Equal(1, stats.WeekStreak);
    }

    [Fact]
    public void FirstWeek_NormNotMet_IsNotAMiss()
    {
        var stats = Run("1 3 0");

        Assert.Equal("NDC", Marks(stats, 3));
        Assert.Equal(1, stats.WeekStreak);
        Assert.False(stats.WeekPaused);
    }

    [Fact]
    public void FirstWeek_Neutral_ThenMiss_IsFirstMissPause()
    {
        var stats = Run("0 0 0");

        Assert.Equal("NPC", Marks(stats, 3));
        Assert.True(stats.WeekPaused);
    }

    [Fact]
    public void FirstWeek_CreatedOnSunday_SameRule()
    {
        // Создана в воскресенье: неделя почти вся прошла, но это всё равно первая неделя — не пропуск
        var stats = Run("0 3 0", firstDay: W(-2).AddDays(6));

        Assert.Equal("NDC", Marks(stats, 3));
    }

    [Fact]
    public void NewHabit_OnlyCurrentWeek()
    {
        var stats = Run("2");

        Assert.Equal("C", Marks(stats, 1));
        Assert.Equal((0, false, 0, false), (stats.WeekStreak, stats.WeekPaused, stats.MonthStreak, stats.MonthPaused));
        Assert.Equal(2, stats.AveragePerWeek); // завершённых недель нет — среднее по текущей
    }

    // ---------- Серия недель: пауза и обрыв ----------

    [Fact]
    public void FirstMiss_IsPause_StreakAliveButDoesNotGrow()
    {
        var stats = Run("333 0 0");

        Assert.Equal("DDDPC", Marks(stats, 5));
        Assert.Equal(3, stats.WeekStreak);
        Assert.True(stats.WeekPaused);
    }

    [Fact]
    public void PauseThenDone_StreakContinues_CountsOnlyDoneWeeks()
    {
        var stats = Run("33 0 33 0");

        Assert.Equal("DDPDDC", Marks(stats, 6));
        Assert.Equal(4, stats.WeekStreak);
        Assert.False(stats.WeekPaused);
    }

    [Fact]
    public void SecondMissInARow_BreaksStreak_PauseBecomesMiss()
    {
        var stats = Run("333 0 0 0");

        Assert.Equal("DDDMMC", Marks(stats, 6));
        Assert.Equal(0, stats.WeekStreak);
        Assert.False(stats.WeekPaused); // серии нет — и паузы нет
    }

    [Fact]
    public void AfterBreak_NewStreakFromOne()
    {
        var stats = Run("333 00 3 0");

        Assert.Equal("DDDMMDC", Marks(stats, 7));
        Assert.Equal(1, stats.WeekStreak);
    }

    [Fact]
    public void LongBreak_AllMissesAreMisses()
    {
        var stats = Run("3 0000 0");

        Assert.Equal("DMMMMC", Marks(stats, 6));
        Assert.Equal(0, stats.WeekStreak);
    }

    [Fact]
    public void MissesNotInARow_OnlyPauses()
    {
        var stats = Run("3 0 3 0 3 0 0");

        Assert.Equal("DPDPDPC", Marks(stats, 7));
        Assert.Equal(3, stats.WeekStreak);
        Assert.True(stats.WeekPaused);
    }

    [Fact]
    public void PartialWeek_BelowNorm_IsAMiss()
    {
        var stats = Run("33 2 2 0");

        Assert.Equal("DDMMC", Marks(stats, 5));
    }

    [Fact]
    public void MockupTrainingStrip_Streak13()
    {
        // Лента «Тренировок» из макета: DDDDWDDDDMMDDDDDWDDDDDDDDN → серия 13
        var stats = Run("3333 0 3333 00 33333 0 33333333 2");

        Assert.Equal("DDDDPDDDDMMDDDDDPDDDDDDDDC", Marks(stats, 26));
        Assert.Equal(13, stats.WeekStreak);
        Assert.False(stats.WeekPaused);
    }

    // ---------- Каникулы ----------

    [Fact]
    public void VacationWeek_FourDaysOrMore_FreezesStreak()
    {
        // Каникулы пн–чт позапрошлой недели (4 дня)
        var stats = Run("33 0 3 0", vacations: [new(W(-2), W(-2).AddDays(3))]);

        Assert.Equal("DDVDC", Marks(stats, 5));
        Assert.Equal(3, stats.WeekStreak);
    }

    [Fact]
    public void VacationWeek_ThreeDays_IsNotVacation()
    {
        var stats = Run("33 0 3 0", vacations: [new(W(-2), W(-2).AddDays(2))]);

        Assert.Equal("DDPDC", Marks(stats, 5));
    }

    [Fact]
    public void Vacation_SpanningTwoWeeks_OnlyWeekWithFourDays()
    {
        // С четверга по среду: в первой неделе 4 дня (чт–вс), во второй — 3 (пн–ср)
        var stats = Run("3 0 0 0", vacations: [new(W(-2).AddDays(3), W(-1).AddDays(2))]);

        Assert.Equal("DVPC", Marks(stats, 4));
        Assert.True(stats.WeekPaused);
    }

    [Fact]
    public void VacationWeek_EvenWithNormMet_IsVacation_ClipsStillInTotal()
    {
        var stats = Run("3 5 3 0", vacations: [new(W(-2), W(-2).AddDays(6))]);

        Assert.Equal("DVDC", Marks(stats, 4));
        Assert.Equal(2, stats.WeekStreak);
        Assert.Equal(11, stats.Total);
    }

    [Fact]
    public void MissesAroundVacation_AreInARow()
    {
        // Пропуск, каникулы, снова пропуск — второй подряд: серия прервана
        var stats = Run("3 0 0 0 0", vacations: [new(W(-2), W(-2).AddDays(6))]);

        Assert.Equal("DMVMC", Marks(stats, 5));
        Assert.Equal(0, stats.WeekStreak);
    }

    [Fact]
    public void PauseBeforeVacation_StaysPausedAfter()
    {
        var stats = Run("33 0 0 0", vacations: [new(W(-1), W(-1).AddDays(6))]);

        Assert.Equal("DDPVC", Marks(stats, 5));
        Assert.True(stats.WeekPaused);
        Assert.Equal(2, stats.WeekStreak);
    }

    [Fact]
    public void FirstWeek_Vacation_IsVacation()
    {
        var stats = Run("0 3 0", vacations: [new(W(-2), W(-2).AddDays(5))]);

        Assert.Equal("VDC", Marks(stats, 3));
        Assert.Equal(1, stats.WeekStreak);
    }

    [Fact]
    public void VacationUntil_WhenTodayIsInVacation()
    {
        var stats = Run("3 0", vacations: [new(Today.AddDays(-2), Today.AddDays(10)), new(W(-1), W(-1).AddDays(1))]);

        Assert.Equal(Today.AddDays(10), stats.VacationUntil);
        Assert.Null(Run("3 0", vacations: [new(W(-1), W(-1).AddDays(6))]).VacationUntil);
    }

    // ---------- Месяцы ----------

    [Fact]
    public void MonthOf_WeekBelongsToMonthOfItsThursday()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), HabitStatsCalculator.MonthOf(new DateOnly(2026, 9, 28))); // чт — 1 октября
        Assert.Equal(new DateOnly(2026, 9, 1), HabitStatsCalculator.MonthOf(new DateOnly(2026, 9, 21)));
        Assert.Equal(new DateOnly(2026, 1, 1), HabitStatsCalculator.MonthOf(new DateOnly(2025, 12, 29))); // стык годов
    }

    // Недели по месяцам (по четвергам): июнь — 4 недели (с 1 июня), июль — 5, август — 4, сентябрь — 4,
    // октябрь — 28 сентября и текущая (5 октября). От 1 июня до текущей — 19 недель.

    [Fact]
    public void Months_AllGood_StreakCountsFinishedMonths_CurrentInProgress()
    {
        var stats = Run("3333|33333|3333|3333|3 0");

        Assert.Equal("__DDDDC", MonthMarks(stats, 7));
        Assert.Equal(4, stats.MonthStreak);
        Assert.Equal(new DateOnly(2026, 10, 1), stats.Months[^1].Month);
        Assert.Equal(HabitStatsCalculator.MonthsShown, stats.Months.Count);
    }

    [Fact]
    public void Month_WithOnlyPause_IsCounted()
    {
        var stats = Run("3333|33033|3333|3333|3 0");

        Assert.Equal("DDDDC", MonthMarks(stats, 5));
        Assert.Equal(4, stats.MonthStreak);
    }

    [Fact]
    public void Month_WithBreak_NotCounted_FirstTimeIsPause()
    {
        var stats = Run("3333|33003|3333|3333|3 0");

        Assert.Equal("DPDDC", MonthMarks(stats, 5));
        Assert.Equal(3, stats.MonthStreak);
        Assert.False(stats.MonthPaused); // после пропущенного июля были засчитанные месяцы
    }

    [Fact]
    public void LastMonthNotCounted_MonthPaused()
    {
        var stats = Run("3333|33333|3333|3003|3 0");

        Assert.Equal("DDDPC", MonthMarks(stats, 5));
        Assert.Equal(3, stats.MonthStreak);
        Assert.True(stats.MonthPaused);
    }

    [Fact]
    public void TwoMonthsInARow_BreakMonthStreak_PauseBecomesMiss()
    {
        var stats = Run("3333|33333|0033|3003|3 0");

        Assert.Equal("DDMMC", MonthMarks(stats, 5));
        Assert.Equal(0, stats.MonthStreak);
    }

    [Fact]
    public void BreakAcrossMonthBorder_CountsAgainstMonthWhereItBroke()
    {
        // Последняя неделя августа — пауза, первая неделя сентября — второй пропуск: прервалась в сентябре
        var stats = Run("3333|33333|3330|0333|3 0");

        Assert.Equal("DDDPC", MonthMarks(stats, 5));
        Assert.Equal(3, stats.MonthStreak);
        Assert.True(stats.MonthPaused);
    }

    [Fact]
    public void MonthOfVacations_IsVacation_MonthStreakFrozen()
    {
        // Весь июль — каникулы (29 июня … 2 августа: пять недель июля)
        var stats = Run("3333|00000|3333|3333|3 0", vacations: [new(new(2026, 6, 29), new(2026, 8, 2))]);

        Assert.Equal("DVDDC", MonthMarks(stats, 5));
        Assert.Equal(3, stats.MonthStreak);
        Assert.Equal("DDDDVVVVVDDDD", Marks(stats, 19)[..13]);
        Assert.Equal(13, stats.WeekStreak); // 4 июня + 4 августа + 4 сентября + 28 сентября
    }

    [Fact]
    public void MonthPartlyVacation_JudgedByOtherWeeks()
    {
        // Две недели июля — каникулы, остальные засчитаны — месяц засчитан
        var stats = Run("3333|33003|3333|3333|3 0", vacations: [new(new(2026, 7, 13), new(2026, 7, 26))]);

        Assert.Equal("DDDDC", MonthMarks(stats, 5));
    }

    [Fact]
    public void FirstMonth_PartialWithNeutralWeekOnly_IsNeutral()
    {
        // Копилка создана в последнюю неделю августа (чт — 27 августа), неделя без нормы
        var stats = Run("1|3333|3 0");

        Assert.Equal("__NDC", MonthMarks(stats, 5));
        Assert.Equal(1, stats.MonthStreak);
    }

    [Fact]
    public void MonthsBeforeStart_AreBeforeStart()
    {
        var stats = Run("3333|3 0");

        Assert.Equal("_____DC", MonthMarks(stats, 7));
    }

    [Fact]
    public void CurrentMonth_EvenWithBreak_IsInProgress()
    {
        // Сентябрь хороший; в октябре (28 сентября и текущая) — пропуск и текущая
        var stats = Run("3333|0 0", firstDay: new DateOnly(2026, 8, 31));

        Assert.Equal("DC", MonthMarks(stats, 2));
        Assert.True(stats.WeekPaused);
        Assert.Equal(1, stats.MonthStreak);
    }

    [Fact]
    public void YearBorder_WeekOfNewYearBelongsToJanuary()
    {
        // Неделя 29 декабря 2025 – 4 января 2026: четверг 1 января — январь
        var today = new DateOnly(2026, 1, 14);
        var input = new HabitInput(new DateOnly(2025, 12, 1), 1, 30, 0,
            [new(2025, 12, 1), new(2025, 12, 8), new(2025, 12, 15), new(2025, 12, 22), new(2025, 12, 29), new(2026, 1, 5)], []);

        var stats = HabitStatsCalculator.Calculate(input, today);

        Assert.Equal([new(2025, 12, 1), new(2026, 1, 1)], stats.Months.TakeLast(2).Select(m => m.Month));
        Assert.Equal(PeriodResult.Done, stats.Months[^2].Result); // декабрь: 4 недели
        Assert.Equal(PeriodResult.Current, stats.Months[^1].Result);
        Assert.Equal(1, stats.ThisMonth); // по календарю в январе — 5 января (29 декабря — в декабре)
        Assert.Equal(1, stats.ThisYear);
    }

    // ---------- Средние и периоды ----------

    [Fact]
    public void Average_OverFinishedWeeks_WithoutVacationAndNeutralFirstWeek()
    {
        // Первая неделя (1) нейтральная, потом 4, 5, каникулы (6), 0 — среднее (4 + 5 + 0) / 3 = 3
        var stats = Run("1 4 5 6 0 9", vacations: [new(W(-2), W(-2).AddDays(6))]);

        Assert.Equal(3, stats.AveragePerWeek);
    }

    [Fact]
    public void Average_RoundedToTenth()
    {
        var stats = Run("3 4 4 0");

        Assert.Equal(3.7, stats.AveragePerWeek);
    }

    [Fact]
    public void ThisMonthAndYear_ByCalendar()
    {
        var stats = Run("3 0", extraClips: [new(2026, 10, 1), new(2026, 9, 30), new(2026, 1, 2), new(2025, 12, 31)]);

        // 3 скрепки прошлой недели — 28–30 сентября; 1 октября — октябрь
        Assert.Equal(1, stats.ThisMonth);
        Assert.Equal(6, stats.ThisYear);
    }

    [Fact]
    public void CreatedAfterToday_EverythingBeforeStart()
    {
        var input = new HabitInput(Today.AddDays(14), 3, 30, 5, [], []);

        var stats = HabitStatsCalculator.Calculate(input, Today);

        Assert.All(stats.Weeks, w => Assert.Equal(PeriodResult.BeforeStart, w.Result));
        Assert.All(stats.Months, m => Assert.Equal(PeriodResult.BeforeStart, m.Result));
        Assert.Equal(5, stats.Total);
    }

    // ---------- Норма у каждой недели своя (правки цели «с этой недели и дальше») ----------

    [Fact]
    public void WeekNorms_EachWeekJudgedByItsOwnNorm()
    {
        // Норму снизили до 2, потом подняли до 3: прошлые недели не пересчитываются
        var stats = Run("3 2 2 0", weekNorms: [3, 2, 3, 3]);

        Assert.Equal("DDPC", Marks(stats, 4));
        Assert.Equal((2, true), (stats.WeekStreak, stats.WeekPaused));
    }

    [Fact]
    public void WeekNorms_RaisedNorm_DoesNotBreakPastWeeks()
    {
        var withHistory = Run("2 2 2 1", weekNorms: [2, 2, 5, 5]);
        var withoutHistory = Run("2 2 2 1", norm: 5);

        Assert.Equal("DDPC", Marks(withHistory, 4));
        Assert.Equal(2, withHistory.WeekStreak);
        Assert.Equal("NMMC", Marks(withoutHistory, 4)); // одна норма на всё — прошлые недели «провалены»
    }

    [Fact]
    public void WeekNorms_CurrentWeek_RewardByItsNorm()
    {
        var stats = Run("3 2", weekNorms: [3, 2]);

        Assert.Equal(2, stats.WeekStreak);
    }

    [Fact]
    public void WeekWithoutGoal_NeitherDoneNorMiss_StreakFrozen()
    {
        // Цель удалили «только на этой неделе»
        var stats = Run("3 0 3 0", weekNorms: [3, 0, 3, 3]);

        Assert.Equal("DNDC", Marks(stats, 4));
        Assert.Equal((2, false), (stats.WeekStreak, stats.WeekPaused));
    }

    [Fact]
    public void EndedSeries_WeeksAfterEnd_NotMisses_HistoryKept()
    {
        // Сняли «каждую неделю» две недели назад: дальше целей нет — серия замерла, не оборвалась
        var stats = Run("3 3 0 0 0", weekNorms: [3, 3, 0, 0, 0]);

        Assert.Equal("DDNNC", Marks(stats, 5));
        Assert.Equal((2, false), (stats.WeekStreak, stats.WeekPaused));
        Assert.Equal(6, stats.Total);
    }

    [Fact]
    public void ResumedSeries_HistoryContinues()
    {
        // Перерыв (целей не было), потом снова «каждую неделю»
        var stats = Run("3 0 0 3 3", weekNorms: [3, 0, 0, 3, 3]);

        Assert.Equal("DNNDC", Marks(stats, 5));
        Assert.Equal(3, stats.WeekStreak);
    }
}
