namespace Planner.Core.Services;

/// <summary>Куда положить дело при создании или перетаскивании.</summary>
public abstract record TaskTarget;

/// <summary>В ленту «Когда-нибудь»: категория и позиция (0 — наверх).</summary>
public sealed record SomedayTarget(int CategoryId, int Index) : TaskTarget;

/// <summary>В день: позиция среди дел БЕЗ времени (дела со временем сортируются по времени сами).</summary>
public sealed record DayTarget(DateOnly Date, int Index) : TaskTarget;
