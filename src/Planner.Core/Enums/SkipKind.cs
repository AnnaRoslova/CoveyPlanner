namespace Planner.Core.Enums;

/// <summary>Вид пропуска дела (docs/specs/skips.md, K1).</summary>
public enum SkipKind
{
    /// <summary>«Не надо»: необходимость отпала — дело выпадает из плана (не скрепка и не пропуск привычки).</summary>
    NotNeeded = 1,

    /// <summary>«Не сделала»: надо было, но не вышло — для привычки и цели это пропуск.</summary>
    Missed = 2,
}
