namespace Planner.Core.Enums;

/// <summary>Вид пропуска дела (docs/specs/skips.md, K1).</summary>
public enum SkipKind
{
    /// <summary>«Не нужно»: необходимость отпала — дело выпадает из плана (не скрепка и не пропуск привычки).</summary>
    NotNeeded = 1,

    /// <summary>«Не сделано»: надо было, но не вышло — для привычки и цели это пропуск.</summary>
    Missed = 2,
}
