using System.Numerics;

namespace Planner.Core.Enums;

/// <summary>Дни недели, по которым у постоянной цели сами встают занятия (набор: можно несколько).</summary>
[Flags]
public enum Weekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,

    /// <summary>«Каждый день».</summary>
    All = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday,
}

public static class WeekdaysExtensions
{
    /// <summary>День недели даты как элемент набора.</summary>
    public static Weekdays Of(DateOnly date) => (Weekdays)(1 << (((int)date.DayOfWeek + 6) % 7));

    public static bool Includes(this Weekdays days, DateOnly date) => (days & Of(date)) != 0;

    /// <summary>Сколько дней в наборе.</summary>
    public static int Count(this Weekdays days) => BitOperations.PopCount((uint)(days & Weekdays.All));
}
