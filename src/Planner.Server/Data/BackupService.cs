using System.Globalization;
using Microsoft.Data.Sqlite;
using Planner.Server.Data.Storage;

namespace Planner.Server.Data;

/// <summary>Куда класть резервные копии и сколько хранить.</summary>
public sealed record BackupOptions(string Directory, int Keep);

/// <summary>
/// Резервные копии — снимок папкой (docs/specs/accounts.md, D5): planner-2026-10-22_221500-вручную\ со всем, что нужно для восстановления:
/// accounts.db, каждый users\*.db (и planner.db, пока он не перенесён), папка keys\ (ключи Data Protection, hmac.key), local.key, settings.json.
/// Базы копируются средствами SQLite (VACUUM INTO) — целостно даже при работающем сервере и режиме WAL.
/// Снимок собирается во временной папке и только потом получает своё имя: недоделанный снимок не выглядит готовым.
/// Хранятся последние <see cref="BackupOptions.Keep"/> снимков.
/// </summary>
public sealed class BackupService(DataPaths paths, BackupOptions options, TimeProvider clock, ILogger<BackupService> logger)
{
    private const string Prefix = "planner-";
    private const string PartialSuffix = ".partial";

    /// <summary>Сделать снимок сейчас. reason попадает в имя папки: planner-2026-09-30_221500-вручную.</summary>
    public async Task<string> CreateAsync(string reason, CancellationToken ct = default)
    {
        var stamp = clock.GetLocalNow().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(options.Directory, $"{Prefix}{stamp}-{reason}");
        var partial = target + PartialSuffix;
        DeleteDirectory(partial);
        Directory.CreateDirectory(partial);

        foreach (var database in Databases())
        {
            await VacuumIntoAsync(database, Path.Combine(partial, Path.GetRelativePath(paths.Root, database)), ct);
        }

        if (Directory.Exists(paths.KeysDirectory))
        {
            CopyDirectory(paths.KeysDirectory, Path.Combine(partial, Path.GetFileName(paths.KeysDirectory)));
        }

        foreach (var file in new[] { paths.LocalKeyFile, paths.SettingsFile }.Where(File.Exists))
        {
            File.Copy(file, Path.Combine(partial, Path.GetFileName(file)));
        }

        DeleteDirectory(target);
        Directory.Move(partial, target);
        logger.LogInformation("Резервная копия: {Path}", target);

        Prune();
        return target;
    }

    /// <summary>Снимок «за сегодня», если его ещё нет (вызывается при запуске сервера).</summary>
    public async Task EnsureDailyAsync(CancellationToken ct = default)
    {
        var today = clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!List().Any(path => Path.GetFileName(path).StartsWith($"{Prefix}{today}_", StringComparison.Ordinal)))
        {
            await CreateAsync("ежедневная", ct);
        }
    }

    /// <summary>Все снимки, свежие первыми (копии-файлы до аккаунтов, planner-….db, не считаются и не удаляются).</summary>
    public IReadOnlyList<string> List() =>
        Directory.Exists(options.Directory)
            ? Directory.GetDirectories(options.Directory, $"{Prefix}*")
                .Where(path => !path.EndsWith(PartialSuffix, StringComparison.Ordinal))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList()
            : [];

    /// <summary>Базы, которые есть сейчас: аккаунты, файлы пользователей и не перенесённый ещё planner.db.</summary>
    private IEnumerable<string> Databases() =>
        new[] { paths.AccountsDatabase, paths.LegacyDatabase }.Where(File.Exists).Concat(paths.ExistingUserDatabases());

    private static async Task VacuumIntoAsync(string database, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Без пула: снимок не должен держать файлы открытыми (planner.db после переноса переименовывается, файл удалённого аккаунта стирается)
        await using var connection = new SqliteConnection($"{DataPaths.ConnectionString(database)};Pooling=False");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO @target";
        command.Parameters.AddWithValue("@target", target);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void Prune()
    {
        foreach (var old in List().Skip(Math.Max(1, options.Keep)))
        {
            Directory.Delete(old, recursive: true);
            logger.LogInformation("Удалена старая резервная копия: {Path}", old);
        }
    }
}
