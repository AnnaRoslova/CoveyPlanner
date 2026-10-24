namespace Planner.Core.Accounts;

/// <summary>
/// Сессия — одно вошедшее устройство (docs/specs/accounts.md, S1–S5): 90 дней, продлевается работой, но не дольше года с входа.
/// IP не храним — только подпись устройства из User-Agent.
/// </summary>
public class Session
{
    public const int DeviceMaxLength = 60;

    public static readonly TimeSpan SlidingLifetime = TimeSpan.FromDays(90);

    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(365);

    /// <summary>Отметка «последняя активность» — не чаще раза в 5 минут (S3).</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private Session()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>«Edge · Windows», «Safari · iPhone».</summary>
    public string Device { get; private set; } = "";

    public DateTime CreatedAt { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public DateTime AbsoluteExpiresAt => CreatedAt + AbsoluteLifetime;

    public static Session Create(Guid userId, string device, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Device = device.Length > DeviceMaxLength ? device[..DeviceMaxLength] : device,
        CreatedAt = now,
        LastSeenAt = now,
        ExpiresAt = now + SlidingLifetime,
    };

    public bool IsActive(DateTime now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Работа продлевает сессию (S2); true — запись изменилась и её надо сохранить.</summary>
    public bool Touch(DateTime now)
    {
        if (now - LastSeenAt < TouchInterval)
        {
            return false;
        }

        LastSeenAt = now;
        var sliding = now + SlidingLifetime;
        ExpiresAt = sliding < AbsoluteExpiresAt ? sliding : AbsoluteExpiresAt;
        return true;
    }

    public void Revoke(DateTime now) => RevokedAt ??= now;
}

/// <summary>Подпись устройства из User-Agent (S5): браузер и система, без версий.</summary>
public static class DeviceLabel
{
    public static string FromUserAgent(string? userAgent)
    {
        var ua = userAgent ?? "";
        var browser =
            ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : ua.Contains("OPR/", StringComparison.Ordinal) ? "Opera"
            : ua.Contains("YaBrowser/", StringComparison.Ordinal) ? "Яндекс Браузер"
            : ua.Contains("Firefox/", StringComparison.Ordinal) || ua.Contains("FxiOS/", StringComparison.Ordinal) ? "Firefox"
            : ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS/", StringComparison.Ordinal) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
            : "Браузер";
        var system =
            ua.Contains("iPhone", StringComparison.Ordinal) ? "iPhone"
            : ua.Contains("iPad", StringComparison.Ordinal) ? "iPad"
            : ua.Contains("Android", StringComparison.Ordinal) ? "Android"
            : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : ua.Contains("Mac OS X", StringComparison.Ordinal) ? "Mac"
            : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : null;
        return system is null ? browser : $"{browser} · {system}";
    }
}
