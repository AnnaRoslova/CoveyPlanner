namespace Planner.Core.Services;

/// <summary>Куда положить дело при создании или перетаскивании.</summary>
public abstract record TaskTarget;

/// <summary>В ленту «Когда-нибудь»: категория и позиция (0 — наверх).</summary>
public sealed record SomedayTarget(int CategoryId, int Index) : TaskTarget;

/// <summary>В «Приоритеты дня»: день и позиция в списке.</summary>
public sealed record DayPrioritiesTarget(DateOnly Date, int Index) : TaskTarget;

/// <summary>В сетку часов. Время и длительность выравниваются по настройкам сетки.</summary>
public sealed record GridTarget(DateOnly Date, TimeOnly Start, int DurationMinutes) : TaskTarget;
