using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Planner.Server.Data;
using Planner.Server.Data.Storage;
using Planner.Shared;
using BackupDto = Planner.Server.Endpoints.BackupEndpoints.BackupDto;

namespace Planner.Tests.Api;

/// <summary>Резервные копии — снимки папкой со всеми файлами (docs/specs/accounts.md, D5, D6, T6).</summary>
public class BackupTests
{
    /// <summary>Часы, которые идут вперёд на секунду при каждом обращении (разные имена копий).</summary>
    private sealed class TickingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 30, 21, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => _now = _now.AddSeconds(1);
    }

    /// <summary>Часы, которые стоят: за секунду до полуночи — оба запуска сервера в тесте «в один день» (batch-3.md, FT1).</summary>
    private sealed class StoppedClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(2026, 10, 22, 23, 59, 59, TimeSpan.Zero);
    }

    private static int CountRoles(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Roles";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Файлы снимка — пути относительно его папки, через «/».</summary>
    private static List<string> FilesOf(string snapshot) =>
        Directory.GetFiles(snapshot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(snapshot, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public async Task ManualBackup_HasEveryUserFile_Accounts_Keys_LocalKey()
    {
        await using var factory = new PlannerApiFactory();
        var client = factory.CreateScriptClient();
        (await (await factory.CreateSignedInClientAsync()).PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Идеи", "💡")))
            .EnsureSuccessStatusCode();

        // Второй пользователь (его файл); ключи Data Protection и hmac.key уже созданы входом
        var paths = factory.Paths;
        var admin = Path.GetFileName(Assert.Single(paths.ExistingUserDatabases()));
        var other = $"{Guid.NewGuid():N}.db";
        await using (var db = PlannerDbContextFactory.Create(Path.Combine(paths.UsersDirectory, other), TimeProvider.System))
        {
            await db.Database.MigrateAsync();
        }

        TestDatabase.ClearPool(Path.Combine(paths.UsersDirectory, other));

        var backup = await ReadAsync<BackupDto>(await client.PostAsync("/api/backup", null));

        Assert.StartsWith(factory.BackupDirectory, backup.Path);
        Assert.Contains("вручную", backup.File);
        var keys = Directory.GetFiles(paths.KeysDirectory, "*", SearchOption.AllDirectories)
            .Select(f => "keys/" + Path.GetRelativePath(paths.KeysDirectory, f).Replace('\\', '/'))
            .ToList();
        Assert.Contains("keys/hmac.key", keys);
        Assert.Contains(keys, k => k.StartsWith("keys/data-protection/key-", StringComparison.Ordinal));
        string[] expected = ["accounts.db", "local.key", $"users/{admin}", $"users/{other}", .. keys];
        Assert.Equal(expected.Order(StringComparer.Ordinal), FilesOf(backup.Path));
        Assert.Equal(8, CountRoles(Path.Combine(backup.Path, "users", admin))); // целая база администратора
        Assert.Equal(await File.ReadAllTextAsync(paths.HmacKeyFile), await File.ReadAllTextAsync(Path.Combine(backup.Path, "keys", "hmac.key")));

        var list = await client.GetFromJsonAsync<List<BackupDto>>("/api/backup");
        Assert.Equal(backup.File, Assert.Single(list!).File);
    }

    [Fact]
    public async Task Backup_WithoutLocalKey_IsForbidden()
    {
        await using var factory = new PlannerApiFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/backup", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/backup")).StatusCode);

        client.DefaultRequestHeaders.Add(LocalKey.Header, "не тот");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/backup", null)).StatusCode);
        Assert.False(Directory.Exists(factory.BackupDirectory) && Directory.GetDirectories(factory.BackupDirectory).Length > 0);
    }

    [Fact]
    public async Task OldBackups_ArePruned()
    {
        var dir = Path.Combine(Path.GetTempPath(), "planner-tests", $"prune-{Guid.NewGuid():N}");
        var paths = new DataPaths(dir);
        Directory.CreateDirectory(paths.UsersDirectory);
        try
        {
            await using (var db = PlannerDbContextFactory.Create(paths.UserDatabase(Guid.NewGuid()), TimeProvider.System))
            {
                await db.Database.MigrateAsync();
            }

            var service = new BackupService(paths, new BackupOptions(Path.Combine(dir, "backups"), Keep: 2),
                new TickingClock(), NullLogger<BackupService>.Instance);

            var first = await service.CreateAsync("тест");
            await service.CreateAsync("тест");
            var third = await service.CreateAsync("тест");

            var left = service.List();
            Assert.Equal(2, left.Count);
            Assert.Equal(third, left[0]);          // свежие первыми
            Assert.DoesNotContain(first, left);    // самая старая удалена
            Assert.False(Directory.Exists(first));
        }
        finally
        {
            TestDatabase.DeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Startup_DailyBackup_OncePerDay()
    {
        await using var first = new PlannerApiFactory(dailyBackup: true, clock: new StoppedClock());
        first.CreateClient();
        var daily = Assert.Single(Directory.GetDirectories(first.BackupDirectory));
        Assert.Contains("ежедневная", daily);
        Assert.Contains("accounts.db", FilesOf(daily));
        Assert.Contains(FilesOf(daily), f => f.StartsWith("users/", StringComparison.Ordinal));

        // Второй запуск в тот же день на тех же данных и с той же папкой копий — нового снимка нет
        await using var second = new PlannerApiFactory(first.DataDirectory, dailyBackup: true, first.BackupDirectory, new StoppedClock());
        second.CreateClient();
        Assert.Equal(daily, Assert.Single(Directory.GetDirectories(first.BackupDirectory)));
    }

    [Fact]
    public async Task Startup_BacksUpBeforeTransferAndMigrations()
    {
        // planner.db (до аккаунтов) в старой схеме (до GreenLotusPalette) с данными
        var dataDirectory = Path.Combine(Path.GetTempPath(), "planner-tests", $"data-{Guid.NewGuid():N}");
        var legacy = new DataPaths(dataDirectory).LegacyDatabase;
        Directory.CreateDirectory(dataDirectory);
        await using (var db = new PlannerDbContext(new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite($"Data Source={legacy}").Options))
        {
            await db.GetService<IMigrator>().MigrateAsync("RemoveGridAndInbox");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Работа', '#8ac2c4', 0, NULL);");
        }

        TestDatabase.ClearPool(legacy);

        try
        {
            await using var factory = new PlannerApiFactory(dataDirectory, dailyBackup: true);
            factory.CreateClient(); // запуск: снимок → перенос → миграции (ежедневный не нужен — снимок за сегодня уже есть)

            var snapshot = Assert.Single(Directory.GetDirectories(factory.BackupDirectory));
            Assert.Contains("перед-обновлением", snapshot);
            Assert.Equal(["local.key", "planner.db"], FilesOf(snapshot));

            // В снимке — данные в старой схеме (без последних миграций)
            var copy = Path.Combine(snapshot, "planner.db");
            await using var old = new PlannerDbContext(new DbContextOptionsBuilder<PlannerDbContext>()
                .UseSqlite($"Data Source={copy};Pooling=False").Options);
            var applied = await old.Database.GetAppliedMigrationsAsync();
            Assert.DoesNotContain(applied, m => m.EndsWith("GreenLotusPalette", StringComparison.Ordinal));
            Assert.Equal(1, CountRoles(copy));
        }
        finally
        {
            TestDatabase.DeleteDirectory(dataDirectory);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
