using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;
using Planner.Core.Errors;
using Planner.Server.Accounts;
using Planner.Server.Data.Accounts;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>
/// Пользователи и приглашения (docs/specs/accounts.md, U1–U4, I2–I3) — только администратору. Чужих данных планировщика здесь нет (U4).
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization(PlannerClaims.AdminPolicy);

        admin.MapGet("/users", async (AccountsDbContext accounts, CancellationToken ct) =>
            (await accounts.Users.OrderBy(u => u.CreatedAt).ToListAsync(ct))
            .Select(u => new UserAdminDto(u.Id, u.Email, u.CreatedAt, u.LastLoginAt, u.IsAdmin, u.IsDisabled, u.DeleteAfter))
            .ToList());

        // Отключить — сразу отозвать все его сессии; себя (администратора) — нельзя (U3)
        admin.MapPut("/users/{id:guid}/disabled", async (Guid id, SetUserDisabledRequest request, AccountsDbContext accounts,
            SessionService sessions, CancellationToken ct) =>
        {
            var user = await accounts.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
                       ?? throw new DomainException("Пользователь не найден.");
            user.SetDisabled(request.IsDisabled);
            await accounts.SaveChangesAsync(ct);
            if (request.IsDisabled)
            {
                await sessions.RevokeAllAsync(id, ct);
            }

            return new UserAdminDto(user.Id, user.Email, user.CreatedAt, user.LastLoginAt, user.IsAdmin, user.IsDisabled, user.DeleteAfter);
        });

        admin.MapGet("/invites", async (InviteService invites, AccountsDbContext accounts, TimeProvider clock, CancellationToken ct) =>
        {
            var emails = await accounts.Users.ToDictionaryAsync(u => u.Id, u => u.Email, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            return (await invites.ListAsync(ct)).Select(i => ToDto(i, emails, now)).ToList();
        });

        admin.MapPost("/invites", async (CreateInviteRequest request, InviteService invites, TimeProvider clock, HttpContext context,
            CancellationToken ct) =>
        {
            var (invite, link) = await invites.CreateAsync(request.Email, request.Days, $"{context.Request.Scheme}://{context.Request.Host}", ct);
            return new CreatedInviteDto(ToDto(invite, new Dictionary<Guid, string>(), clock.GetUtcNow().UtcDateTime), link);
        });

        admin.MapPost("/invites/{id:int}/revoke", async (int id, InviteService invites, CancellationToken ct) =>
        {
            await invites.RevokeAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static InviteDto ToDto(Invite invite, IReadOnlyDictionary<Guid, string> emails, DateTime now) => new(
        invite.Id, invite.Email, invite.CreatedAt, invite.ExpiresAt, invite.StatusAt(now).ToString(),
        invite.UsedByUserId is { } id && emails.TryGetValue(id, out var email) ? email : null, invite.UsedAt);
}
