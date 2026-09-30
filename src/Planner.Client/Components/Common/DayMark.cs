namespace Planner.Client.Components.Common;

/// <summary>Метка дня занятия цели («Чт»); выполненное занятие показывается темнее.</summary>
public sealed record DayMark(string Label, bool IsDone);
