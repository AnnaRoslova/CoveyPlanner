using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Planner.Server.Data;
using Planner.Shared;

namespace Planner.Tests.Api;

public class BackupTests
{
    /// <summary>Часы, которые идут вперёд на секунду при каждом обращении (разные имена файлов копий).</summary>
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

    [Fact]
    public async Task ManualBackup_IsCompleteDatabaseFile()
    {
        await using var factory = new PlannerApiFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Идеи", "💡"));

        var response = await client.PostAsync("/api/backup", null);
        response.EnsureSuccessStatusCode();
        var backup = await response.Content.ReadFromJsonAsync<Planner.Server.Endpoints.BackupEndpoints.BackupDto>();

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup.Path));
        Assert.StartsWith(factory.BackupDirectory, backup.Path);
        Assert.Contains("вручную", backup.File);
        Assert.Equal(8, CountRoles(backup.Path)); // начальные роли на месте — это целая база

        var list = await client.GetFromJsonAsync<List<Planner.Server.Endpoints.BackupEndpoints.BackupDto>>("/api/backup");
        Assert.Equal(backup.File, Assert.Single(list!).File);
    }

    [Fact]
    public async Task OldBackups_ArePruned()
    {
        var dir = Path.Combine(Path.GetTempPath(), "planner-tests", $"prune-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(dir, "planner.db");
        Directory.CreateDirectory(dir);
        try
        {
            await using (var db = new PlannerDbContext(new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite($"Data Source={dbPath}").Options))
            {
                await db.Database.MigrateAsync();
                var service = new BackupService(db, new BackupOptions(Path.Combine(dir, "backups"), Keep: 2),
                    new TickingClock(), NullLogger<BackupService>.Instance);

                var first = await service.CreateAsync("тест");
                await service.CreateAsync("тест");
                var third = await service.CreateAsync("тест");

                var left = service.List();
                Assert.Equal(2, left.Count);
                Assert.Equal(third, left[0]);          // свежие первыми
                Assert.DoesNotContain(first, left);    // самая старая удалена
            }
        }
        finally
        {
            TestDatabase.ClearPool(dbPath);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_DailyBackup_OncePerDay()
    {
        await using var first = new PlannerApiFactory(dailyBackup: true, clock: new StoppedClock());
        first.CreateClient();
        var daily = Assert.Single(Directory.GetFiles(first.BackupDirectory));
        Assert.Contains("ежедневная", daily);

        // Второй запуск в тот же день на той же базе и с той же папкой копий — новой копии нет
        await using var second = new PlannerApiFactory(first.DatabasePath, dailyBackup: true, first.BackupDirectory, new StoppedClock());
        second.CreateClient();
        Assert.Equal(daily, Assert.Single(Directory.GetFiles(first.BackupDirectory)));
    }

    [Fact]
    public async Task Startup_BacksUpBeforeMigrations()
    {
        // Существующая база в старой схеме (до GreenLotusPalette) с данными
        var dbPath = Path.Combine(Path.GetTempPath(), "planner-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using (var db = new PlannerDbContext(new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite($"Data Source={dbPath}").Options))
        {
            await db.GetService<IMigrator>().MigrateAsync("RemoveGridAndInbox");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Работа', '#8ac2c4', 0, NULL);");
        }

        TestDatabase.ClearPool(dbPath);

        await using var factory = new PlannerApiFactory(dbPath, dailyBackup: true);
        factory.CreateClient(); // запуск сервера: копия → миграции (ежедневная не нужна — копия за сегодня уже есть)

        var files = Directory.GetFiles(factory.BackupDirectory).Select(Path.GetFileName).ToList();
        var beforeUpdate = Assert.Single(files);
        Assert.Contains("перед-обновлением", beforeUpdate);

        // В копии «перед обновлением» — данные в старой схеме (без последних миграций)
        await using var old = new PlannerDbContext(new DbContextOptionsBuilder<PlannerDbContext>()
            .UseSqlite($"Data Source={Path.Combine(factory.BackupDirectory, beforeUpdate!)};Pooling=False").Options);
        var applied = await old.Database.GetAppliedMigrationsAsync();
        Assert.DoesNotContain(applied, m => m.EndsWith("GreenLotusPalette", StringComparison.Ordinal));
        Assert.Equal(1, CountRoles(Path.Combine(factory.BackupDirectory, beforeUpdate!)));
    }
}
