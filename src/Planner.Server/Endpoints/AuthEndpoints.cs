using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;
using Planner.Server.Accounts;
using Planner.Server.Data.Accounts;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Вход кодом на почту и сессии (docs/specs/accounts.md, L, S); почтовый ящик разработки (M2).</summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapGet("/info", (HttpContext context) => new AuthInfoDto(AuthSetup.IsDevMailboxAvailable(context)))
            .AllowAnonymous();

        // Кто вошёл; без входа — 401 (клиент показывает экран входа)
        auth.MapGet("/me", async (HttpContext context, AccountsDbContext accounts, CancellationToken ct) =>
                AuthSetup.UserIdOf(context.User) is { } id && await accounts.Users.FirstOrDefaultAsync(u => u.Id == id, ct) is { } user
                    ? Results.Ok(new MeDto(user.Id, user.Email, user.IsAdmin))
                    : Results.Unauthorized())
            .AllowAnonymous();

        auth.MapPost("/code", async (RequestCodeRequest request, LoginService login, HttpContext context, CancellationToken ct) =>
            {
                var result = await login.RequestCodeAsync(request.Email, ct: ct);
                if (result.Throttled)
                {
                    await AuthSetup.WriteTooManyAsync(context, result.RetryAfter);
                    return Results.Empty;
                }

                return Results.Ok(new RequestCodeResponse((int)result.RetryAfter.TotalSeconds));
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.CodePolicy);

        auth.MapPost("/verify", async (VerifyCodeRequest request, LoginService login, SessionService sessions, HttpContext context, CancellationToken ct) =>
            {
                return await SignInByCodeAsync(await login.CheckCodeAsync(request.Email, request.Code, ct: ct), sessions, context, ct);
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.VerifyPolicy);

        // Регистрация по приглашению (I4–I5)
        auth.MapPost("/invite/check", async (InviteTokenRequest request, InviteService invites, CancellationToken ct) =>
                await invites.FindActiveAsync(request.Token, ct) is { } invite
                    ? new InviteCheckDto(true, invite.Email)
                    : new InviteCheckDto(false, null))
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.VerifyPolicy);

        auth.MapPost("/invite/code", async (InviteCodeRequest request, InviteService invites, LoginService login,
                AccountsDbContext accounts, TimeProvider clock, HttpContext context, CancellationToken ct) =>
            {
                var email = User.NormalizeEmail(request.Email);
                var invite = await invites.FindActiveAsync(request.Token, ct);
                if (invite is null)
                {
                    return Problem("Приглашение не действует — попросите новое", "invite-invalid", 0);
                }

                if (!invite.Accepts(email, clock.GetUtcNow().UtcDateTime))
                {
                    return Problem("Это приглашение — для другой почты", "invite-other-email", 0);
                }

                if (await accounts.Users.AnyAsync(u => u.Email == email, ct))
                {
                    return Problem("У этого адреса уже есть аккаунт", "registered", 0);
                }

                var result = await login.RequestCodeAsync(email, invite, ct);
                if (result.Throttled)
                {
                    await AuthSetup.WriteTooManyAsync(context, result.RetryAfter);
                    return Results.Empty;
                }

                return Results.Ok(new RequestCodeResponse((int)result.RetryAfter.TotalSeconds));
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.CodePolicy);

        auth.MapPost("/invite/verify", async (InviteVerifyRequest request, InviteService invites, LoginService login,
                SessionService sessions, TimeProvider clock, HttpContext context, CancellationToken ct) =>
            {
                var invite = await invites.FindActiveAsync(request.Token, ct);
                if (invite is null || !invite.Accepts(request.Email, clock.GetUtcNow().UtcDateTime))
                {
                    return Problem("Приглашение не действует — попросите новое", "invite-invalid", 0);
                }

                return await SignInByCodeAsync(await login.CheckCodeAsync(request.Email, request.Code, invite, ct), sessions, context, ct);
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.VerifyPolicy);

        auth.MapPost("/logout", async (HttpContext context, SessionService sessions, CancellationToken ct) =>
        {
            if (AuthSetup.SessionIdOf(context.User) is { } id)
            {
                await sessions.RevokeAsync(id, ct);
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        auth.MapPost("/logout-all", async (HttpContext context, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.RevokeAllAsync(AuthSetup.UserIdOf(context.User)!.Value, ct);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    /// <summary>Почтовый ящик разработки (M2): страница и последнее письмо (для e2e) — только в режиме Folder и только с этого компьютера.</summary>
    public static void MapDevMailbox(this WebApplication app)
    {
        app.MapGet("/dev/mailbox", (HttpContext context, DevMailbox mailbox) =>
            AuthSetup.IsDevMailboxAvailable(context) ? Results.Content(Page(mailbox.Latest()), "text/html; charset=utf-8") : Results.NotFound());

        app.MapGet("/dev/mailbox/latest", (string? to, HttpContext context, DevMailbox mailbox) =>
            AuthSetup.IsDevMailboxAvailable(context) && mailbox.Latest(1, to).FirstOrDefault() is { } letter
                ? Results.Ok(letter)
                : Results.NotFound());
    }

    /// <summary>Код верный — сессия и cookie; иначе — понятная ошибка (L4).</summary>
    private static async Task<IResult> SignInByCodeAsync(CodeCheckResult result, SessionService sessions, HttpContext context, CancellationToken ct)
    {
        switch (result.Check)
        {
            case LoginCheck.Ok:
                var user = result.User!;
                var session = await sessions.CreateAsync(user.Id, context.Request.Headers.UserAgent, ct);
                await AuthSetup.SignInAsync(context, user, session);
                return Results.Ok(new MeDto(user.Id, user.Email, user.IsAdmin, result.Restored));
            case LoginCheck.Wrong:
                return Problem($"Неверный код, осталось попыток: {result.RemainingAttempts}", "wrong", result.RemainingAttempts);
            default:
                return Problem("Код больше не действует — запросите новый", "expired", 0);
        }
    }

    private static IResult Problem(string title, string code, int remaining) => Results.Problem(
        title: title,
        statusCode: StatusCodes.Status400BadRequest,
        extensions: new Dictionary<string, object?> { ["code"] = code, ["remainingAttempts"] = remaining });

    private static string Page(IReadOnlyList<MailboxLetter> letters)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Почтовый ящик разработки</title>
            <style>body{font:16px/1.5 system-ui,sans-serif;max-width:640px;margin:24px auto;padding:0 16px;color:#3e4d52;background:#f6fbf9}
            article{background:#fff;border-radius:14px;padding:12px 16px;margin:12px 0}small{color:#657579}pre{white-space:pre-wrap;font:inherit;margin:8px 0 0}</style>
            </head><body><h1>Почтовый ящик разработки</h1>
            <p><small>Письма не отправляются, а складываются сюда (Mail:Mode = Folder). Свежие сверху.</small></p>
            """);
        if (letters.Count == 0)
        {
            html.Append("<p>Писем пока нет.</p>");
        }

        foreach (var letter in letters)
        {
            html.Append("<article><small>").Append(WebUtility.HtmlEncode($"{letter.At:dd.MM HH:mm:ss} · {letter.To}")).Append("</small>")
                .Append("<div><b>").Append(WebUtility.HtmlEncode(letter.Subject)).Append("</b></div>")
                .Append("<pre>").Append(WebUtility.HtmlEncode(letter.Body)).Append("</pre></article>");
        }

        return html.Append("</body></html>").ToString();
    }
}
