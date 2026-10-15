namespace Planner.Core.Services;

/// <summary>Куда положить дело при создании или перетаскивании.</summary>
public abstract record TaskTarget;

/// <summary>В ленту «Когда-нибудь»: категория (null — «неразобранное») и позиция (0 — наверх).</summary>
public sealed record SomedayTarget(int? CategoryId, int Index) : TaskTarget;

/// <summary>В день: позиция среди дел БЕЗ времени (дела со временем сортируются по времени сами).</summary>
public sealed record DayTarget(DateOnly Date, int Index) : TaskTarget;

/// <summary>На неделю без дня (N4): WeekStart — любой день недели, приводится к понедельнику; в конец дел этой недели без дня.</summary>
/// RoleId — бросили на строку «на неделе» роли: явное действие — дело получает эту роль (если её ещё нет).
public sealed record WeekTarget(DateOnly WeekStart, int? RoleId = null) : TaskTarget;

/// <summary>В список шагов дела <paramref name="ParentId"/>, без дня: позиция в списке (S20).</summary>
public sealed record StepListTarget(int ParentId, int Index) : TaskTarget;
