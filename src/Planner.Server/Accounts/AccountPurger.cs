using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Storage;

namespace Planner.Server.Accounts;

/// <summary>
/// Стирание аккаунтов, у которых кончилась «корзина» (docs/specs/accounts.md, P3): файл данных (с журналом), сессии, коды, строка пользователя.
/// Из резервных копий данные уходят по их сроку хранения (D5).
/// </summary>
public sealed class AccountPurger(AccountsDbContext accounts, DataPaths paths, TimeProvider clock, ILogger<AccountPurger> logger)
{
    public async Task<int> PurgeDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await accounts.Users.Where(u => u.DeleteAfter != null && u.DeleteAfter <= now).ToListAsync(ct);
        foreach (var user in due)
        {
            var file = paths.UserDatabase(user.Id);
            using (var connection = new SqliteConnection(DataPaths.ConnectionString(file)))
            {
                SqliteConnection.ClearPool(connection); // иначе пул держит файл открытым
            }

            foreach (var path in new[] { file, file + "-wal", file + "-shm" }.Where(File.Exists))
            {
                File.Delete(path);
            }

            accounts.LoginCodes.RemoveRange(accounts.LoginCodes.Where(c => c.Email == user.Email));
            accounts.Users.Remove(user); // сессии — каскадом
            logger.LogInformation("Аккаунт {Email} стёрт по окончании корзины", EmailMask.Mask(user.Email));
        }

        await accounts.SaveChangesAsync(ct);
        return due.Count;
    }
}

/// <summary>Проверка «корзины аккаунтов» при запуске и раз в час (P3).</summary>
public sealed class AccountPurgeWorker(IServiceScopeFactory scopes, ILogger<AccountPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AccountPurger>().PurgeDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Не удалось стереть аккаунты с истёкшей корзиной");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
