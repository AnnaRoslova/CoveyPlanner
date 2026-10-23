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

    public static User Create(string email, bool isAdmin, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        Email = NormalizeEmail(email),
        IsAdmin = isAdmin,
        CreatedAt = now,
    };

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
