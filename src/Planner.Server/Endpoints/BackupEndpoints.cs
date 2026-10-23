using Planner.Server.Data;
using Planner.Server.Data.Storage;

namespace Planner.Server.Endpoints;

/// <summary>
/// Резервные копии (их же делает сервер при запуске — раз в день и перед обновлением схемы).
/// Только по ключу служебных скриптов из local.key (docs/specs/accounts.md, D6).
/// </summary>
public static class BackupEndpoints
{
    public sealed record BackupDto(string File, string Path);

    public static void MapBackupEndpoints(this RouteGroupBuilder api)
    {
        var backup = api.MapGroup("/backup").AddEndpointFilter(async (context, next) =>
            LocalKey.Matches(context.HttpContext, context.HttpContext.RequestServices.GetRequiredService<DataPaths>())
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
