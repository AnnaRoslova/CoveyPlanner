namespace Planner.Server.Data;

/// <summary>
/// Часы сервера: системные плюс сдвиг. Сдвиг задаётся только в Development (POST /api/dev/clock) — e2e «давно лежат»
/// записывает мысли «в прошлом»; в обычной работе сдвиг всегда 0.
/// </summary>
public sealed class ShiftableTimeProvider : TimeProvider
{
    public TimeSpan Shift { get; set; }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Shift;

    public override TimeZoneInfo LocalTimeZone => System.LocalTimeZone;
}
