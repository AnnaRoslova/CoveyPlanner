using Planner.Core.Errors;
using Planner.Core.Habits;

namespace Planner.Core.Entities;

/// <summary>
/// Каникулы привычек: с <see cref="From"/> по <see cref="To"/> включительно. Неделя, в которую попало не меньше 4 дней
/// каникул, не засчитывается и не считается пропуском — серия замирает (см. <see cref="HabitStatsCalculator"/>).
/// Каникулы — для всех привычек или для выбранных (<see cref="Habits"/>: id привычки — id её серии; пусто — для всех).
/// </summary>
public class Vacation
{
    /// <summary>Самые длинные каникулы — год.</summary>
    public const int MaxDays = 366;

    private readonly List<VacationHabit> _habits = [];

    private Vacation()
    {
    }

    public int Id { get; private set; }

    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    /// <summary>Привычки (id серий), к которым относятся каникулы (таблица VacationHabits); пусто — ко всем.</summary>
    public IReadOnlyCollection<VacationHabit> Habits => _habits;

    public IReadOnlyList<int> HabitIds => _habits.Select(h => h.HabitId).Order().ToList();

    public DateRange Range => new(From, To);

    public static Vacation Create(DateOnly from, DateOnly to, IEnumerable<int> habitIds)
    {
        if (to < from)
        {
            throw new DomainException("Каникулы заканчиваются раньше, чем начинаются.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            throw new DomainException($"Каникулы — не длиннее {MaxDays} дней.");
        }

        var vacation = new Vacation { From = from, To = to };
        vacation._habits.AddRange(habitIds.Distinct().Select(id => new VacationHabit(id)));
        return vacation;
    }

    public bool AppliesTo(int habitId) => _habits.Count == 0 || _habits.Any(h => h.HabitId == habitId);

    /// <summary>
    /// Убрать привычку (её удалили). false — каникулы были только у неё и теперь ни к чему не относятся:
    /// их нужно удалить (иначе пустой список значил бы «для всех»).
    /// </summary>
    public bool RemoveHabit(int habitId)
    {
        if (_habits.RemoveAll(h => h.HabitId == habitId) == 0)
        {
            return true;
        }

        return _habits.Count > 0;
    }
}

/// <summary>Привычка (серия), к которой относятся каникулы (таблица VacationHabits). Живёт только внутри каникул.</summary>
public class VacationHabit
{
    // Для EF Core
    private VacationHabit()
    {
    }

    public VacationHabit(int habitId) => HabitId = habitId;

    public int HabitId { get; private set; }
}
