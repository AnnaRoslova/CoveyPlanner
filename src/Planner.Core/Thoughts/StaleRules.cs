using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Core.Thoughts;

/// <summary>
/// Давно лежащие мысли (docs/specs/batch-ux.md, BS). Мысль — дело ленты (не шаг, не проект). Старая, если
/// max(UpdatedAt, KeptAt) + срок ≤ сейчас (BS5); напоминают о ней один раз за перешагивание срока (BS4).
/// </summary>
public static class StaleRules
{
    public const int DefaultMonths = 3;
    public const int MinMonths = 1;
    public const int MaxMonths = 36;

    public static bool IsThought(TaskItem task) => task is { State: TaskState.Someday, ParentId: null, IsProject: false };

    /// <summary>С какого момента мысль «старая»: срок от последнего изменения или «Оставить».</summary>
    public static DateTime StaleSince(TaskItem task, int months)
    {
        var fresh = task.KeptAt is { } kept && kept > task.UpdatedAt ? kept : task.UpdatedAt;
        return fresh.AddMonths(months);
    }

    public static bool IsStale(TaskItem task, int months, DateTime nowUtc) => IsThought(task) && StaleSince(task, months) <= nowUtc;

    /// <summary>Нужна плашка: мысль старая, а о нынешнем перешагивании срока ещё не напоминали.</summary>
    public static bool NeedsNotice(TaskItem task, int months, DateTime nowUtc) =>
        IsStale(task, months, nowUtc) && (task.StaleNotifiedAt is not { } notified || notified < StaleSince(task, months));
}
