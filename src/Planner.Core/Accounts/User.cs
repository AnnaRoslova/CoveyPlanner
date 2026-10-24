using Planner.Core.Errors;

namespace Planner.Core.Accounts;

/// <summary>
/// Пользователь (аккаунт): почта и свои данные планировщика — отдельный файл базы (docs/specs/accounts.md, D1–D3).
/// Id — GUID: по имени файла данных нельзя угадать соседа или число пользователей (D2).
/// </summary>
public class User
{
    /// <summary>Самый длинный адрес почты по RFC 5321.</summary>
    public const int EmailMaxLength = 254;

    private User()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Почта в нижнем регистре, без пробелов по краям (L6).</summary>
    public string Email { get; private set; } = "";

    public bool IsAdmin { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    /// <summary>Отключён администратором (U3): вход невозможен, данные не трогаются.</summary>
    public bool IsDisabled { get; private set; }

    /// <summary>«Корзина аккаунта» (P3): удалён, но до этого момента восстанавливается входом по коду; потом данные стираются.</summary>
    public DateTime? DeleteAfter { get; private set; }

    public static readonly TimeSpan DeletionGrace = TimeSpan.FromDays(7);

    /// <summary>«Удалить аккаунт»: отключить на 7 дней; единственного администратора — нельзя.</summary>
    public void MarkDeleted(DateTime now)
    {
        if (IsAdmin)
        {
            throw new DomainException("Вы единственный администратор — аккаунт удалить нельзя.");
        }

        DeleteAfter ??= now + DeletionGrace;
    }

    /// <summary>Вход по коду в эти 7 дней возвращает аккаунт.</summary>
    public bool Restore()
    {
        var wasDeleted = DeleteAfter is not null;
        DeleteAfter = null;
        return wasDeleted;
    }

    public void SetDisabled(bool disabled)
    {
        if (disabled && IsAdmin)
        {
            throw new DomainException("Администратора отключить нельзя.");
        }

        IsDisabled = disabled;
    }

    public static User Create(string email, bool isAdmin, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        Email = NormalizeEmail(email),
        IsAdmin = isAdmin,
        CreatedAt = now,
    };

    /// <summary>Успешный вход кодом (L7).</summary>
    public void RecordLogin(DateTime now) => LastLoginAt = now;

    /// <summary>Почта для сравнения и хранения: без пробелов по краям, в нижнем регистре; явная опечатка — ошибка.</summary>
    public static string NormalizeEmail(string? email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var at = normalized.IndexOf('@');
        if (normalized.Length > EmailMaxLength
            || at <= 0 || at != normalized.LastIndexOf('@') || at == normalized.Length - 1
            || normalized.Any(char.IsWhiteSpace))
        {
            throw new DomainException("Похоже, в адресе почты опечатка.");
        }

        return normalized;
    }
}
