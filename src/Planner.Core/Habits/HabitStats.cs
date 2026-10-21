namespace Planner.Core.Habits;

/// <summary>Отрезок дат включительно (каникулы).</summary>
public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly date) => date >= From && date <= To;
}

/// <summary>Что известно о привычке для подсчёта: всё, что нужно калькулятору, без сущностей и базы.</summary>
/// <param name="FirstDay">Первый день привычки: недели раньше его недели не считаются.</param>
/// <param name="WeeklyNorm">Норма — сколько раз в неделю (для всех недель, если не задано <paramref name="WeekNorms"/>).</param>
/// <param name="Clips">Дни скрепок — по одной дате на каждое выполненное дело (повторы — несколько скрепок в день).</param>
/// <param name="Vacations">Каникулы, которые относятся к этой привычке.</param>
/// <param name="WeekNorms">
/// Норма каждой недели (понедельник → «раз в неделю» цели той недели): правка цели «с этой недели и дальше» прошлые недели
/// не меняет — они оцениваются по своей тогдашней норме. Недели, которой здесь нет (цель удалили «только на этой неделе»,
/// серия закончилась), — без нормы: не засчитана и не пропуск. null — у всех недель <paramref name="WeeklyNorm"/>.
/// </param>
public sealed record HabitInput(
    DateOnly FirstDay,
    int WeeklyNorm,
    int JarSize,
    int StartCount,
    IReadOnlyCollection<DateOnly> Clips,
    IReadOnlyCollection<DateRange> Vacations,
    IReadOnlyDictionary<DateOnly, int>? WeekNorms = null)
{
    /// <summary>Норма недели <paramref name="weekStart"/>; null — у недели нормы нет (цели не было).</summary>
    public int? NormOf(DateOnly weekStart) =>
        WeekNorms is null ? WeeklyNorm
        : WeekNorms.TryGetValue(weekStart, out var norm) ? norm
        : null;
}

/// <summary>Итог недели (или месяца).</summary>
public enum PeriodResult
{
    /// <summary>До начала привычки — не считается.</summary>
    BeforeStart,

    /// <summary>Засчитана: норма выполнена (месяц — серия недель в нём не прервалась).</summary>
    Done,

    /// <summary>
    /// Ни засчитана, ни пропуск: первая неделя привычки без нормы (она неполная), неделя без цели (нормы нет), месяц из одних таких недель.
    /// На серию не влияет.
    /// </summary>
    Neutral,

    /// <summary>Первый пропуск — ⚠ пауза: серия жива, но не растёт.</summary>
    Paused,

    /// <summary>
    /// Второй пропуск подряд (и дальше) — серия прервана. Пауза перед ним тоже показывается так:
    /// задним числом это был не перерыв, а начало обрыва.
    /// </summary>
    Missed,

    /// <summary>Каникулы: не засчитывается и не пропуск — серия замирает.</summary>
    Vacation,

    /// <summary>Текущая неделя (месяц): в процессе, серию не ломает.</summary>
    Current,
}

/// <summary>Неделя привычки: понедельник, сколько скрепок, итог, норма той недели (null — у недели нет цели).</summary>
public sealed record HabitWeek(DateOnly WeekStart, int Count, PeriodResult Result, int? Norm = null);

/// <summary>Месяц привычки: первое число месяца и итог. Неделя относится к месяцу своего четверга.</summary>
public sealed record HabitMonth(DateOnly Month, PeriodResult Result);

/// <summary>Посчитанная статистика привычки.</summary>
/// <param name="Total">Всего скрепок за всё время: стартовое число + выполненные дела.</param>
/// <param name="FullJars">Полных банок на полке.</param>
/// <param name="InJar">Скрепок в текущей банке.</param>
/// <param name="ThisWeek">Скрепок на текущей неделе.</param>
/// <param name="WeekStreak">🔥 Серия недель: засчитанных недель с начала серии; текущая входит, как только норма в ней выполнена.</param>
/// <param name="BestWeekStreak">Лучшая серия недель за всю историю (дашборд, batch-ux.md BD2).</param>
/// <param name="WeekPaused">⚠ Пауза: последняя завершённая неделя (не считая каникул) — первый пропуск, а норма текущей ещё не выполнена.</param>
/// <param name="MonthStreak">🌙 Серия месяцев.</param>
/// <param name="MonthPaused">⚠ Пауза серии месяцев: последний завершённый месяц не засчитан (первый раз).</param>
/// <param name="Weeks">Последние недели (до <see cref="HabitStatsCalculator.WeeksShown"/>), последняя — текущая.</param>
/// <param name="Months">Последние месяцы (до <see cref="HabitStatsCalculator.MonthsShown"/>), последний — текущий.</param>
/// <param name="AveragePerWeek">В среднем за неделю — по завершённым неделям без каникул (и без неполной первой без нормы).</param>
/// <param name="ThisMonth">Скрепок в этом календарном месяце.</param>
/// <param name="ThisYear">Скрепок в этом календарном году.</param>
/// <param name="VacationUntil">Сегодня каникулы — до какого дня; null — не каникулы.</param>
public sealed record HabitStats(
    int Total,
    int FullJars,
    int InJar,
    int ThisWeek,
    int WeekStreak,
    int BestWeekStreak,
    bool WeekPaused,
    int MonthStreak,
    bool MonthPaused,
    IReadOnlyList<HabitWeek> Weeks,
    IReadOnlyList<HabitMonth> Months,
    double AveragePerWeek,
    int ThisMonth,
    int ThisYear,
    DateOnly? VacationUntil);
