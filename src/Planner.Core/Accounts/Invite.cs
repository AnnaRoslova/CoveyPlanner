using Planner.Core.Errors;

namespace Planner.Core.Accounts;

/// <summary>Состояние приглашения в списке администратора (docs/specs/accounts.md, I3).</summary>
public enum InviteStatus
{
    Active,
    Used,
    Expired,
    Revoked,
}

/// <summary>Режим регистрации (I1): только по приглашениям (по умолчанию) или любой адрес.</summary>
public enum RegistrationMode
{
    Invite,
    Open,
}

/// <summary>
/// Приглашение (I2–I5): одноразовая ссылка со сроком, можно привязать к почте. В базе — только хэш токена из ссылки:
/// сама ссылка показывается один раз, при создании. Тратится только при успешном коде (I4).
/// </summary>
public class Invite
{
    public static readonly int[] DayChoices = [1, 3, 7, 14, 30];

    public const int DefaultDays = 7;

    private Invite()
    {
    }

    public int Id { get; private set; }

    public string TokenHash { get; private set; } = "";

    /// <summary>Почта, для которой приглашение; null — для любой незарегистрированной.</summary>
    public string? Email { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public Guid? UsedByUserId { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public static Invite Create(string tokenHash, string? email, int days, DateTime now)
    {
        if (!DayChoices.Contains(days))
        {
            throw new DomainException($"Срок приглашения — {string.Join(", ", DayChoices)} дней.");
        }

        return new Invite
        {
            TokenHash = tokenHash,
            Email = string.IsNullOrWhiteSpace(email) ? null : User.NormalizeEmail(email),
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
        };
    }

    public InviteStatus StatusAt(DateTime now) =>
        UsedAt is not null ? InviteStatus.Used
        : RevokedAt is not null ? InviteStatus.Revoked
        : now >= ExpiresAt ? InviteStatus.Expired
        : InviteStatus.Active;

    /// <summary>Подходит ли приглашение этой почте сейчас (I4–I5).</summary>
    public bool Accepts(string email, DateTime now) =>
        StatusAt(now) == InviteStatus.Active && (Email is null || Email == User.NormalizeEmail(email));

    public void Use(Guid userId, DateTime now)
    {
        UsedAt = now;
        UsedByUserId = userId;
    }

    public void Revoke(DateTime now)
    {
        if (StatusAt(now) == InviteStatus.Active)
        {
            RevokedAt = now;
        }
    }
}
