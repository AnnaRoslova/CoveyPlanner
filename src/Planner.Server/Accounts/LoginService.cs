using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;
using Planner.Server.Data.Accounts;

namespace Planner.Server.Accounts;

/// <summary>Итог запроса кода: отправлен (или «как будто отправлен» — L1) либо слишком рано (L5).</summary>
public sealed record CodeRequestResult(bool Throttled, TimeSpan RetryAfter);

/// <summary>Итог проверки кода; User — только при <see cref="LoginCheck.Ok"/>; Restored — вход вернул аккаунт из «корзины» (P3).</summary>
public sealed record CodeCheckResult(LoginCheck Check, int RemainingAttempts, User? User, bool Restored = false);

/// <summary>
/// Вход кодом на почту (docs/specs/accounts.md, L1–L7) и регистрация (I4, I6). Ответ на запрос кода одинаков для любой почты:
/// кому код не положен (не зарегистрирован в режиме Invite, отключён), письмо не уходит, но запись (без хэша) и частота — те же.
/// </summary>
public sealed class LoginService(
    AccountsDbContext accounts,
    CodeHasher hasher,
    IEmailSender mail,
    AccountService accountService,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<LoginService> logger)
{
    /// <summary>Режим регистрации (appsettings / settings.json → "Accounts:Registration": Invite | Open).</summary>
    public const string RegistrationKey = "Accounts:Registration";

    private RegistrationMode Mode => configuration.GetValue(RegistrationKey, RegistrationMode.Invite);

    /// <summary>Прислать код. invite — регистрация по приглашению (проверено вызывающим): код положен и незарегистрированной почте.</summary>
    public async Task<CodeRequestResult> RequestCodeAsync(string email, Invite? invite = null, CancellationToken ct = default)
    {
        email = User.NormalizeEmail(email);
        var now = clock.GetUtcNow().UtcDateTime;

        var recent = await accounts.LoginCodes
            .Where(c => c.Email == email && c.CreatedAt > now.AddHours(-1))
            .ToListAsync(ct);
        if (LoginThrottle.RetryAfter(recent.Select(c => c.CreatedAt).ToList(), now) is { } wait)
        {
            return new CodeRequestResult(Throttled: true, wait);
        }

        foreach (var previous in recent)
        {
            previous.Supersede(now);
        }

        var user = await accounts.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var deserves = user is null ? invite is not null || Mode == RegistrationMode.Open : !user.IsDisabled;
        var code = LoginCode.Generate();
        accounts.LoginCodes.Add(LoginCode.Create(email, deserves ? hasher.Hash(email, code) : null, now));
        await accounts.SaveChangesAsync(ct);

        if (deserves)
        {
            await mail.SendAsync(new EmailMessage(email, $"Код входа: {code[..3]} {code[3..]}",
                $"Код входа в планировщик: {code[..3]} {code[3..]}\r\n\r\n" +
                "Действует 10 минут. Если вы не входили — просто удалите письмо.\r\n"), ct);
        }
        else
        {
            logger.LogInformation("Код для {Email} не отправлен: не зарегистрирован или отключён", EmailMask.Mask(email));
        }

        return new CodeRequestResult(Throttled: false, LoginThrottle.MinInterval);
    }

    /// <summary>
    /// Проверить код. Верный, а пользователя ещё нет — регистрация: по приглашению (оно тратится только здесь, I4) или в режиме Open (I6);
    /// новый пользователь получает стартовый набор (N1).
    /// </summary>
    public async Task<CodeCheckResult> CheckCodeAsync(string email, string code, Invite? invite = null, CancellationToken ct = default)
    {
        email = User.NormalizeEmail(email);
        var now = clock.GetUtcNow().UtcDateTime;
        var latest = await accounts.LoginCodes
            .Where(c => c.Email == email)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (latest is null)
        {
            return new CodeCheckResult(LoginCheck.Expired, 0, null);
        }

        var check = latest.Check(hasher.Hash(email, LoginCode.Clean(code)), now);
        await accounts.SaveChangesAsync(ct);
        if (check != LoginCheck.Ok)
        {
            return new CodeCheckResult(check, latest.RemainingAttempts, null);
        }

        var user = await accounts.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            if (invite is null && Mode != RegistrationMode.Open)
            {
                return new CodeCheckResult(LoginCheck.Expired, 0, null);
            }

            user = await accountService.CreateUserAsync(email, isAdmin: false, ct);
            invite?.Use(user.Id, now);
            logger.LogInformation("Новый пользователь {Email} ({How})", EmailMask.Mask(email), invite is null ? "открытая регистрация" : "по приглашению");
        }
        else if (user.IsDisabled)
        {
            return new CodeCheckResult(LoginCheck.Expired, 0, null);
        }

        var restored = user.Restore();
        user.RecordLogin(now);
        await accounts.SaveChangesAsync(ct);
        return new CodeCheckResult(LoginCheck.Ok, latest.RemainingAttempts, user, restored);
    }
}

/// <summary>Чем кончилась проверка сессии на запросе (S3): жива, недействительна или пользователь отключён (Q5).</summary>
public enum SessionCheck
{
    Ok,
    Invalid,
    Disabled,
}

/// <summary>Сессии (S1–S5): создать при входе, проверять на каждом запросе, отзывать.</summary>
public sealed class SessionService(AccountsDbContext accounts, TimeProvider clock)
{
    public async Task<Session> CreateAsync(Guid userId, string? userAgent, CancellationToken ct = default)
    {
        var session = Session.Create(userId, DeviceLabel.FromUserAgent(userAgent), clock.GetUtcNow().UtcDateTime);
        accounts.Sessions.Add(session);
        await accounts.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Сессия жива, принадлежит пользователю, он не отключён; работа продлевает её (S2–S3).</summary>
    public async Task<SessionCheck> ValidateAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await accounts.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        var user = await accounts.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (session is null || !session.IsActive(now) || user is null || user.DeleteAfter is not null)
        {
            return SessionCheck.Invalid;
        }

        if (user.IsDisabled)
        {
            return SessionCheck.Disabled;
        }

        if (session.Touch(now))
        {
            await accounts.SaveChangesAsync(ct);
        }

        return SessionCheck.Ok;
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await accounts.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        session?.Revoke(clock.GetUtcNow().UtcDateTime);
        await accounts.SaveChangesAsync(ct);
    }

    /// <summary>«Выйти на всех устройствах» (S4), отключение (U3): все сессии пользователя.</summary>
    public async Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var session in await accounts.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct))
        {
            session.Revoke(now);
        }

        await accounts.SaveChangesAsync(ct);
    }
}
