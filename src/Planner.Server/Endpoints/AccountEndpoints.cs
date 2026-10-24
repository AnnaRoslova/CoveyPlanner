using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;
using Planner.Core.Errors;
using Planner.Server.Accounts;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Storage;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Свой аккаунт (docs/specs/accounts.md, P1–P3, S4–S5): устройства, выгрузка данных, удаление («корзина» на 7 дней).</summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var account = api.MapGroup("/account");

        account.MapGet("/sessions", async (HttpContext context, AccountsDbContext accounts, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = AuthSetup.UserIdOf(context.User)!.Value;
            var current = AuthSetup.SessionIdOf(context.User);
            var now = clock.GetUtcNow().UtcDateTime;
            var sessions = await accounts.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
            return sessions.Where(s => s.IsActive(now)).OrderByDescending(s => s.LastSeenAt)
                .Select(s => new SessionDto(s.Id, s.Device, s.CreatedAt, s.LastSeenAt, s.Id == current))
                .ToList();
        });

        // Выйти на одном устройстве (своём или другом) — только своя сессия
        account.MapDelete("/sessions/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext accounts, SessionService sessions,
            CancellationToken ct) =>
        {
            var userId = AuthSetup.UserIdOf(context.User)!.Value;
            if (!await accounts.Sessions.AnyAsync(s => s.Id == id && s.UserId == userId, ct))
            {
                return Results.NotFound();
            }

            await sessions.RevokeAsync(id, ct);
            if (id == AuthSetup.SessionIdOf(context.User))
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return Results.NoContent();
        });

        // P2: всё из файла данных — таблица за таблицей, плюс почта и дата регистрации
        account.MapGet("/export", async (HttpContext context, AccountsDbContext accounts, DataPaths paths, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = AuthSetup.UserIdOf(context.User)!.Value;
            var user = await accounts.Users.FirstAsync(u => u.Id == userId, ct);
            var now = clock.GetLocalNow();
            var export = new Dictionary<string, object?>
            {
                ["format"] = "coveyplanner-export",
                ["version"] = 1,
                ["exportedAt"] = now,
                ["account"] = new { email = user.Email, createdAt = user.CreatedAt },
                ["tables"] = await DumpAsync(paths.UserDatabase(userId), ct),
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions
            {
                WriteIndented = true,
                // Файл для человека: русский текст как есть, а не \uXXXX
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
            });
            return Results.File(json, "application/json", $"coveyplanner-{now:yyyy-MM-dd}.json");
        });

        // P3: подтверждение — своя почта; аккаунт отключается на 7 дней, сессии отозваны
        account.MapPost("/delete", async (DeleteAccountRequest request, HttpContext context, AccountsDbContext accounts,
            SessionService sessions, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = AuthSetup.UserIdOf(context.User)!.Value;
            var user = await accounts.Users.FirstAsync(u => u.Id == userId, ct);
            if (!string.Equals(request.Email?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException("Почта не совпадает с почтой аккаунта.");
            }

            user.MarkDeleted(clock.GetUtcNow().UtcDateTime);
            await accounts.SaveChangesAsync(ct);
            await sessions.RevokeAllAsync(userId, ct);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return new DeletedAccountDto(user.DeleteAfter!.Value);
        });
    }

    /// <summary>Таблицы файла данных: имя → строки (столбец → значение). Служебная sqlite_* не входит.</summary>
    private static async Task<Dictionary<string, List<Dictionary<string, object?>>>> DumpAsync(string path, CancellationToken ct)
    {
        var tables = new Dictionary<string, List<Dictionary<string, object?>>>();
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);
        var names = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        foreach (var name in names)
        {
            var rows = new List<Dictionary<string, object?>>();
            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null
                        : reader.GetValue(i) is byte[] bytes ? Convert.ToBase64String(bytes)
                        : reader.GetValue(i);
                }

                rows.Add(row);
            }

            tables[name] = rows;
        }

        return tables;
    }
}
