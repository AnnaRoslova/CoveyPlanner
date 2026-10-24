using Planner.Server.Accounts;
using Planner.Server.Data;
using Planner.Server.Data.Storage;

namespace Planner.Server.Endpoints;

/// <summary>
/// Резервные копии (их же делает сервер при запуске — раз в день и перед обновлением схемы).
/// Только по ключу служебных скриптов из local.key или администратору (docs/specs/accounts.md, D6).
/// </summary>
public static class BackupEndpoints
{
    public sealed record BackupDto(string File, string Path);

    public static void MapBackupEndpoints(this RouteGroupBuilder api)
    {
        // Скрипты не входят кодом — их пускает ключ; поэтому вход здесь не обязателен, а проверка — в фильтре
        var backup = api.MapGroup("/backup").AllowAnonymous().AddEndpointFilter(async (context, next) =>
            LocalKey.Matches(context.HttpContext, context.HttpContext.RequestServices.GetRequiredService<DataPaths>())
            || context.HttpContext.User.HasClaim(PlannerClaims.Admin, "1")
                ? await next(context)
                : Results.Problem(title: "Нужен ключ служебных скриптов (local.key).", statusCode: StatusCodes.Status403Forbidden));

        // Сделать снимок сейчас (tools/backup.ps1)
        backup.MapPost("", async (BackupService backups, CancellationToken ct) =>
        {
            var path = await backups.CreateAsync("вручную", ct);
            return new BackupDto(Path.GetFileName(path), path);
        });

        // Список снимков, свежие первыми
        backup.MapGet("", (BackupService backups) =>
            backups.List().Select(p => new BackupDto(Path.GetFileName(p), p)).ToList());
    }
}
