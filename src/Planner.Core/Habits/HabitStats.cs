namespace Planner.Core.Habits;

/// <summary>Отрезок дат включительно (каникулы).</summary>
public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly date) => date >= From && date <= To;
}

/// <summary>Что известно о копилке для подсчёта: всё, что нужно калькулятору, без сущностей и базы.</summary>
/// <param name="FirstDay">День создания копилки: недели раньше его недели не считаются.</param>
/// <param name="Clips">Дни скрепок — по одной дате на каждое выполненное дело (повторы — несколько скрепок в день).</param>
/// <param name="Vacations">Каникулы, которые относятся к этой копилке.</param>
public sealed record HabitInput(
    DateOnly FirstDay,
    int WeeklyNorm,
    int JarSize,
    int StartCount,
    IReadOnlyCollection<DateOnly> Clips,
    IReadOnlyCollection<DateRange> Vacations);

/// <summary>Итог недели (или месяца).</summary>
public enum PeriodResult
{
    /// <summary>До создания копилки — не считается.</summary>
    BeforeStart,

    /// <summary>Засчитана: норма выполнена (месяц — серия недель в нём не прервалась).</summary>
    Done,

    /// <summary>
    /// Ни засчитана, ни пропуск: первая неделя копилки без нормы (она неполная), месяц из одних таких недель.
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

/// <summary>Неделя копилки: понедельник, сколько скрепок, итог.</summary>
public sealed record HabitWeek(DateOnly WeekStart, int Count, PeriodResult Result);

/// <summary>Месяц копилки: первое число месяца и итог. Неделя относится к месяцу своего четверга.</summary>
public sealed record HabitMonth(DateOnly Month, PeriodResult Result);

/// <summary>Посчитанная статистика копилки.</summary>
/// <param name="Total">Всего скрепок за всё время: стартовое число + выполненные дела.</param>
/// <param name="FullJars">Полных банок на полке.</param>
/// <param name="InJar">Скрепок в текущей банке.</param>
/// <param name="ThisWeek">Скрепок на текущей неделе.</param>
/// <param name="WeekStreak">🔥 Серия недель: засчитанных недель с начала серии; текущая входит, как только норма в ней выполнена.</param>
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
    bool WeekPaused,
    int MonthStreak,
    bool MonthPaused,
    IReadOnlyList<HabitWeek> Weeks,
    IReadOnlyList<HabitMonth> Months,
    double AveragePerWeek,
    int ThisMonth,
    int ThisYear,
    DateOnly? VacationUntil);
