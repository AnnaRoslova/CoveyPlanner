using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;
using Planner.Server.Data.Accounts;

namespace Planner.Server.Data.Storage;

/// <summary>
/// Подготовить хранилище при запуске (docs/specs/accounts.md, D4, X1, I7): снимок перед обновлением схемы,
/// миграции accounts.db, первый администратор (с переносом planner.db), миграции файла каждого пользователя,
/// ежедневный снимок. Ошибка любого шага — сервер не стартует: лучше не запуститься, чем показать пустой планировщик.
/// </summary>
public sealed class StorageInitializer(
    DataPaths paths,
    AccountsDbContext accounts,
    BackupService backups,
    StorageState state,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<StorageInitializer> logger)
{
    /// <summary>Почта первого администратора (settings.json → "Accounts:AdminEmail"; tools/install.ps1 -AdminEmail).</summary>
    public const string AdminEmailKey = "Accounts:AdminEmail";

    /// <summary>Делать ли копию «за сегодня» при запуске сервера (по умолчанию да).</summary>
    public const string DailyBackupKey = "Storage:DailyBackup";

    /// <summary>Подготовить; если при этом перенесён planner.db — отчёт о переносе.</summary>
    public async Task<TransferReport?> InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.UsersDirectory);
        LocalKey.Ensure(paths);

        // Новые миграции для уже существующих данных (или перенос planner.db) — сначала снимок всего
        if (await NeedsBackupBeforeUpdateAsync(ct))
        {
            await backups.CreateAsync("перед-обновлением", ct);
        }

        await accounts.Database.MigrateAsync(ct);

        TransferReport? report = null;
        if (!await accounts.Users.AnyAsync(ct))
        {
            report = await CreateAdminAsync(ct);
        }

        foreach (var userId in await accounts.Users.Select(u => u.Id).ToListAsync(ct))
        {
            await PrepareUserDatabaseAsync(userId, ct);
        }

        state.AdminId = (await accounts.Users.Where(u => u.IsAdmin).OrderBy(u => u.CreatedAt).FirstAsync(ct)).Id;

        if (configuration.GetValue(DailyBackupKey, true))
        {
            await backups.EnsureDailyAsync(ct);
        }

        return report;
    }

    private async Task<bool> NeedsBackupBeforeUpdateAsync(CancellationToken ct)
    {
        if (File.Exists(paths.LegacyDatabase) && !File.Exists(paths.AccountsDatabase))
        {
            return true;
        }

        if (File.Exists(paths.AccountsDatabase) && await HasPendingAsync(accounts, ct))
        {
            return true;
        }

        foreach (var file in paths.ExistingUserDatabases())
        {
            await using var db = PlannerDbContextFactory.Create(file, clock);
            if (await HasPendingAsync(db, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasPendingAsync(DbContext db, CancellationToken ct) =>
        (await db.Database.GetAppliedMigrationsAsync(ct)).Any() && (await db.Database.GetPendingMigrationsAsync(ct)).Any();

    /// <summary>
    /// Пользователей нет: администратор из настроек; есть planner.db — его данные становятся данными администратора (X1).
    /// Порядок такой, чтобы прерванный перенос не оставил полдела: копия и сверка → строка пользователя → planner.db переименовывается.
    /// </summary>
    private async Task<TransferReport?> CreateAdminAsync(CancellationToken ct)
    {
        var email = configuration[AdminEmailKey];
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(File.Exists(paths.LegacyDatabase)
                ? $"Есть данные {paths.LegacyDatabase}, но не задана почта администратора ({AdminEmailKey}): tools/install.ps1 -AdminEmail … или {paths.SettingsFile}."
                : $"Не задана почта администратора ({AdminEmailKey}): tools/install.ps1 -AdminEmail … или {paths.SettingsFile}.");
        }

        var now = clock.GetUtcNow();
        var admin = User.Create(email, isAdmin: true, now.UtcDateTime);
        TransferReport? report = null;
        if (File.Exists(paths.LegacyDatabase))
        {
            report = await LegacyTransfer.CopyAsync(paths.LegacyDatabase, paths.UserDatabase(admin.Id), admin.Email, clock.GetLocalNow(), ct);
        }

        accounts.Users.Add(admin);
        await accounts.SaveChangesAsync(ct);

        if (report is not null)
        {
            var kept = LegacyTransfer.KeepLegacy(paths, clock.GetLocalNow());
            await File.WriteAllTextAsync(paths.TransferReport, report.ToMarkdown(), ct);
            logger.LogInformation("Данные planner.db перенесены в аккаунт администратора; прежний файл — {Kept}, отчёт — {Report}",
                kept, paths.TransferReport);
        }

        return report;
    }

    /// <summary>Миграции файла пользователя; файла ещё нет — создать с начальными данными.</summary>
    private async Task PrepareUserDatabaseAsync(Guid userId, CancellationToken ct)
    {
        var path = paths.UserDatabase(userId);
        var isNew = !File.Exists(path);
        await using var db = PlannerDbContextFactory.Create(path, clock);
        await db.Database.MigrateAsync(ct);
        if (isNew)
        {
            await SeedData.EnsureSeededAsync(db, ct);
        }
    }
}
