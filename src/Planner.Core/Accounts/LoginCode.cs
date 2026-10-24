using System.Security.Cryptography;
using System.Text;

namespace Planner.Core.Accounts;

/// <summary>Чем кончилась проверка введённого кода (docs/specs/accounts.md, L4).</summary>
public enum LoginCheck
{
    /// <summary>Код верный — входим; код потрачен.</summary>
    Ok,

    /// <summary>Неверный, попытки ещё есть.</summary>
    Wrong,

    /// <summary>Неверный, и это была последняя попытка — код сгорел.</summary>
    Burned,

    /// <summary>Действующего кода нет: истёк, потрачен, сгорел или заменён новым.</summary>
    Expired,
}

/// <summary>
/// Код входа (L2–L4): 6 цифр, 10 минут, 5 попыток, одноразовый; в базе — только хэш (L3).
/// Запрос на незарегистрированную почту тоже оставляет запись — без хэша (письма нет): так частота (L5) и ответы
/// одинаковы для любой почты и по ним нельзя узнать, кто зарегистрирован (L1).
/// </summary>
public class LoginCode
{
    public const int Length = 6;

    public const int MaxAttempts = 5;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private LoginCode()
    {
    }

    public int Id { get; private set; }

    public string Email { get; private set; } = "";

    /// <summary>HMAC кода; null — почта не зарегистрирована, верного кода нет.</summary>
    public string? CodeHash { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public int RemainingAttempts => Math.Max(0, MaxAttempts - Attempts);

    public static LoginCode Create(string email, string? codeHash, DateTime now) => new()
    {
        Email = User.NormalizeEmail(email),
        CodeHash = codeHash,
        CreatedAt = now,
        ExpiresAt = now + Lifetime,
    };

    /// <summary>Шесть случайных цифр (криптографический генератор).</summary>
    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Введённое — только цифры («123 456», «123-456» → «123456»).</summary>
    public static string Clean(string? input) => new((input ?? "").Where(char.IsAsciiDigit).ToArray());

    public bool IsUsable(DateTime now) => UsedAt is null && now < ExpiresAt && Attempts < MaxAttempts;

    /// <summary>Новый код отменяет прежний (L2).</summary>
    public void Supersede(DateTime now)
    {
        if (IsUsable(now))
        {
            ExpiresAt = now;
        }
    }

    public LoginCheck Check(string enteredHash, DateTime now)
    {
        if (!IsUsable(now))
        {
            return LoginCheck.Expired;
        }

        if (CodeHash is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeHash), Encoding.ASCII.GetBytes(enteredHash)))
        {
            UsedAt = now;
            return LoginCheck.Ok;
        }

        Attempts++;
        return Attempts >= MaxAttempts ? LoginCheck.Burned : LoginCheck.Wrong;
    }
}

/// <summary>Частота кодов по почте (L5): не чаще одного в 60 с и пяти в час.</summary>
public static class LoginThrottle
{
    public const int PerHour = 5;

    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    /// <summary>Через сколько можно запросить новый код; null — можно сейчас. requestedAt — запросы этой почты за последний час.</summary>
    public static TimeSpan? RetryAfter(IReadOnlyCollection<DateTime> requestedAt, DateTime now)
    {
        var lastHour = requestedAt.Where(t => t > now - TimeSpan.FromHours(1)).Order().ToList();
        TimeSpan? wait = null;
        if (lastHour.Count > 0 && now - lastHour[^1] < MinInterval)
        {
            wait = MinInterval - (now - lastHour[^1]);
        }

        if (lastHour.Count >= PerHour)
        {
            var hourly = lastHour[^PerHour] + TimeSpan.FromHours(1) - now;
            wait = wait is { } w && w > hourly ? w : hourly;
        }

        return wait;
    }
}
