using Planner.Server.Data;

namespace Planner.Server.Endpoints;

/// <summary>Резервные копии базы (их же делает сервер при запуске — раз в день и перед обновлением схемы).</summary>
public static class BackupEndpoints
{
    public sealed record BackupDto(string File, string Path);

    public static void MapBackupEndpoints(this RouteGroupBuilder api)
    {
        // Сделать копию сейчас (tools/backup.ps1)
        api.MapPost("/backup", async (BackupService backups, CancellationToken ct) =>
        {
            var path = await backups.CreateAsync("вручную", ct);
            return new BackupDto(Path.GetFileName(path), path);
        });

        // Список копий, свежие первыми
        api.MapGet("/backup", (BackupService backups) =>
            backups.List().Select(p => new BackupDto(Path.GetFileName(p), p)).ToList());
    }
}
