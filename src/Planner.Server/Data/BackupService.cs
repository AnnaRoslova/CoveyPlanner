using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Planner.Server.Data;

/// <summary>Куда класть резервные копии и сколько хранить.</summary>
public sealed record BackupOptions(string Directory, int Keep);

/// <summary>
/// Резервные копии базы. Копия делается средствами SQLite (VACUUM INTO) — получается целостный файл
/// даже при работающем сервере и режиме WAL (простое копирование planner.db могло бы потерять
/// последние изменения, лежащие в planner.db-wal). Хранятся последние <see cref="BackupOptions.Keep"/> копий.
/// </summary>
public sealed class BackupService(PlannerDbContext db, BackupOptions options, TimeProvider clock, ILogger<BackupService> logger)
{
    private const string Prefix = "planner-";

    /// <summary>Сделать копию сейчас. reason попадает в имя файла: planner-2026-09-30_221500-вручную.db.</summary>
    public async Task<string> CreateAsync(string reason, CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.Directory);
        var stamp = clock.GetLocalNow().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(options.Directory, $"{Prefix}{stamp}-{reason}.db");

        // VACUUM INTO не пишет поверх существующего файла
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [path], ct);
        logger.LogInformation("Резервная копия базы: {Path}", path);

        Prune();
        return path;
    }

    /// <summary>Копия «за сегодня», если её ещё нет (вызывается при запуске сервера).</summary>
    public async Task EnsureDailyAsync(CancellationToken ct = default)
    {
        var today = clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var exists = Directory.Exists(options.Directory)
                     && Directory.EnumerateFiles(options.Directory, $"{Prefix}{today}_*.db").Any();
        if (!exists)
        {
            await CreateAsync("ежедневная", ct);
        }
    }

    /// <summary>Все копии, свежие первыми.</summary>
    public IReadOnlyList<string> List() =>
        Directory.Exists(options.Directory)
            ? Directory.GetFiles(options.Directory, $"{Prefix}*.db").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    private void Prune()
    {
        foreach (var old in List().Skip(Math.Max(1, options.Keep)))
        {
            File.Delete(old);
            logger.LogInformation("Удалена старая резервная копия: {Path}", old);
        }
    }
}
