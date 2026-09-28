using Planner.Core.Errors;

namespace Planner.Core.Entities;

/// <summary>Настройки сетки часов. В базе одна строка с Id = 1.</summary>
public class UserSettings
{
    public const int SingletonId = 1;

    public static readonly IReadOnlyList<int> AllowedSlotMinutes = [15, 30, 60];

    public int Id { get; private set; } = SingletonId;

    public int DayStartHour { get; private set; } = 7;

    /// <summary>Час окончания сетки (не включительно), максимум 24.</summary>
    public int DayEndHour { get; private set; } = 23;

    public int SlotMinutes { get; private set; } = 30;

    private int DayStartMinutes => DayStartHour * 60;

    private int DayEndMinutes => DayEndHour * 60;

    public void Update(int dayStartHour, int dayEndHour, int slotMinutes)
    {
        if (dayStartHour < 0 || dayEndHour > 24 || dayStartHour >= dayEndHour)
        {
            throw new DomainException("Начало дня должно быть раньше конца, в пределах 0–24 ч.");
        }

        if (!AllowedSlotMinutes.Contains(slotMinutes))
        {
            throw new DomainException($"Шаг сетки: {string.Join(", ", AllowedSlotMinutes)} минут.");
        }

        DayStartHour = dayStartHour;
        DayEndHour = dayEndHour;
        SlotMinutes = slotMinutes;
    }

    /// <summary>Выравнивает начало по шагу сетки (вниз) и прижимает внутрь дня.</summary>
    public TimeOnly SnapStart(TimeOnly start)
    {
        var minutes = start.Hour * 60 + start.Minute;
        minutes = minutes / SlotMinutes * SlotMinutes;
        minutes = Math.Clamp(minutes, DayStartMinutes, DayEndMinutes - SlotMinutes);
        return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
    }

    /// <summary>Округляет длительность до шага (не меньше одного шага) и обрезает по концу дня.</summary>
    public int NormalizeDuration(TimeOnly snappedStart, int durationMinutes)
    {
        var slots = Math.Max(1, (int)Math.Round(durationMinutes / (double)SlotMinutes, MidpointRounding.AwayFromZero));
        var startMinutes = snappedStart.Hour * 60 + snappedStart.Minute;
        var maxDuration = DayEndMinutes - startMinutes;
        return Math.Min(slots * SlotMinutes, maxDuration);
    }
}
