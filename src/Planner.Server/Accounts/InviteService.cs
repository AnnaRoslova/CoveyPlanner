using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;
using Planner.Server.Data.Accounts;

namespace Planner.Server.Accounts;

/// <summary>Приглашения (docs/specs/accounts.md, I2–I5): создать (ссылка — один раз), найти по токену, отозвать.</summary>
public sealed class InviteService(AccountsDbContext accounts, CodeHasher hasher, IEmailSender mail, TimeProvider clock)
{
    /// <summary>Новое приглашение и ссылка «…/invite#токен» (токен — во фрагменте: не попадает в журналы сервера и Referer).</summary>
    public async Task<(Invite Invite, string Link)> CreateAsync(string? email, int days, string origin, CancellationToken ct = default)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var invite = Invite.Create(hasher.HashToken(token), email, days, clock.GetUtcNow().UtcDateTime);
        accounts.Invites.Add(invite);
        await accounts.SaveChangesAsync(ct);

        var link = $"{origin.TrimEnd('/')}/invite#{token}";
        if (invite.Email is not null)
        {
            await mail.SendAsync(new EmailMessage(invite.Email, "Приглашение в планировщик",
                $"Вас пригласили в планировщик. Чтобы зарегистрироваться, откройте ссылку:\r\n\r\n{link}\r\n\r\n" +
                $"Ссылка одноразовая и действует до {invite.ExpiresAt:dd.MM.yyyy}. Если вы не ждали приглашения — просто удалите письмо.\r\n"), ct);
        }

        return (invite, link);
    }

    /// <summary>Действующее приглашение по токену из ссылки; null — нет такого, истекло, использовано, отозвано (I5).</summary>
    public async Task<Invite?> FindActiveAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = hasher.HashToken(token.Trim());
        var invite = await accounts.Invites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        return invite?.StatusAt(clock.GetUtcNow().UtcDateTime) == InviteStatus.Active ? invite : null;
    }

    public async Task<List<Invite>> ListAsync(CancellationToken ct = default) =>
        await accounts.Invites.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).ToListAsync(ct);

    public async Task RevokeAsync(int id, CancellationToken ct = default)
    {
        var invite = await accounts.Invites.FirstOrDefaultAsync(i => i.Id == id, ct)
                     ?? throw new Planner.Core.Errors.NotFoundException("Приглашение", id);
        invite.Revoke(clock.GetUtcNow().UtcDateTime);
        await accounts.SaveChangesAsync(ct);
    }
}
