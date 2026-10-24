using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Planner.Server.Data;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Storage;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Хранилище по пользователям и перенос planner.db в аккаунт администратора (docs/specs/accounts.md, D1–D4, X1–X3, T5).
/// </summary>
public sealed class AccountsTransferTests : IAsyncLifetime
{
    private const string AnnaEmail = "Anna@Example.com ";

    private static readonly DateOnly Monday = new(2026, 9, 28);

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "planner-tests", $"data-{Guid.NewGuid():N}");

    private DataPaths Paths => new(_dataDirectory);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        TestDatabase.DeleteDirectory(_dataDirectory);
        return Task.CompletedTask;
    }

    /// <summary>Данные «как у Анны»: заводятся через API на отдельном сервере, его файл кладётся в нашу папку как planner.db.</summary>
    private async Task<string> CreateLegacyDatabaseAsync()
    {
        await using var source = new PlannerApiFactory();
        var client = await source.CreateSignedInClientAsync();
        var category = await Read<CategoryDto>(await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Книги", "📚")));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Толстой", new SomedayTargetDto(category.Id, 0)));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Бег в парке", new DayTargetDto(Monday, 0)));
        var board = await client.GetStringAsync("/api/board");
        await client.GetStringAsync($"/api/week/{Monday:yyyy-MM-dd}");

        var file = Assert.Single(source.Paths.ExistingUserDatabases());
        Directory.CreateDirectory(_dataDirectory);
        await using (var connection = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO @target";
            command.Parameters.AddWithValue("@target", Paths.LegacyDatabase);
            await command.ExecuteNonQueryAsync();
        }

        return board;
    }

    [Fact]
    public async Task FirstStart_LegacyDataBecomesAdminData_ReportMatches()
    {
        var boardBefore = await CreateLegacyDatabaseAsync();
        var before = await DatabaseFingerprint.ComputeAsync(Paths.LegacyDatabase);

        await using var factory = new PlannerApiFactory(_dataDirectory, adminEmail: AnnaEmail);
        var client = await factory.CreateSignedInClientAsync(AnnaEmail);

        // Экран — тот же, что был до переноса
        Assert.Equal(Normalize(boardBefore), Normalize(await client.GetStringAsync("/api/board")));

        // Администратор с почтой из настроек (в нижнем регистре, без пробелов), его файл — копия planner.db
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var admin = await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Users.SingleAsync();
            Assert.Equal("anna@example.com", admin.Email);
            Assert.True(admin.IsAdmin);
            Assert.Equal(Paths.UserDatabase(admin.Id), Assert.Single(Paths.ExistingUserDatabases()));
        }

        // planner.db не удалён, а переименован и не изменён; отчёт — «всё совпадает»
        Assert.False(File.Exists(Paths.LegacyDatabase));
        Assert.Equal(before, await DatabaseFingerprint.ComputeAsync(Paths.LegacyKeptDatabase));
        var report = await File.ReadAllTextAsync(Paths.TransferReport);
        Assert.Contains("всё совпадает", report);
        Assert.Contains("| Tasks | 2 | 2 |", report);
    }

    [Fact]
    public async Task SecondStart_DoesNotTransferAgain()
    {
        await CreateLegacyDatabaseAsync();
        await using (var first = new PlannerApiFactory(_dataDirectory, adminEmail: AnnaEmail))
        {
            first.CreateClient();
        }

        var report = File.GetLastWriteTimeUtc(Paths.TransferReport);
        var userFile = Assert.Single(Paths.ExistingUserDatabases());

        // Второй запуск: другая почта в настройках ничего не меняет — администратор уже есть
        await using var second = new PlannerApiFactory(_dataDirectory, adminEmail: "other@example.com");
        var board = await (await second.CreateSignedInClientAsync(AnnaEmail)).GetFromJsonAsync<BoardDto>("/api/board");

        Assert.Single(board!.Categories);
        Assert.Equal(userFile, Assert.Single(Paths.ExistingUserDatabases()));
        Assert.Equal(report, File.GetLastWriteTimeUtc(Paths.TransferReport));
        await using var scope = second.Services.CreateAsyncScope();
        Assert.Equal("anna@example.com", (await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task LegacyWithoutAdminEmail_ServerDoesNotStart_DataUntouched()
    {
        await CreateLegacyDatabaseAsync();
        var before = await DatabaseFingerprint.ComputeAsync(Paths.LegacyDatabase);

        await using var factory = new PlannerApiFactory(_dataDirectory, adminEmail: null);
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Accounts:AdminEmail", error.ToString());
        Assert.Equal(before, await DatabaseFingerprint.ComputeAsync(Paths.LegacyDatabase));
        Assert.Empty(Paths.ExistingUserDatabases());
    }

    [Fact]
    public async Task EmptyDataDirectory_AdminGetsSeededFile()
    {
        await using var factory = new PlannerApiFactory(_dataDirectory);
        var board = await (await factory.CreateSignedInClientAsync()).GetFromJsonAsync<BoardDto>("/api/board");

        Assert.Equal(8, board!.Roles.Count);
        Assert.Single(Paths.ExistingUserDatabases());
        Assert.False(File.Exists(Paths.TransferReport));
    }

    [Fact]
    public async Task SettingsFile_InDataDirectory_IsRead()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(Paths.SettingsFile, """{ "Accounts": { "AdminEmail": "from-file@example.com" } }""");

        await using var factory = new PlannerApiFactory(_dataDirectory, adminEmail: null);
        factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal("from-file@example.com", (await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task TransferCommand_OnCopy_WritesReport()
    {
        await CreateLegacyDatabaseAsync();
        var reportPath = Path.Combine(_dataDirectory, "отчёт.md");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DataPaths.DataDirectoryKey] = _dataDirectory,
            [StorageInitializer.AdminEmailKey] = AnnaEmail,
            [StorageInitializer.DailyBackupKey] = "false",
            [DataServiceCollectionExtensions.BackupPathKey] = Path.Combine(_dataDirectory, "backups"),
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration).AddPlannerData();
        await using var provider = services.BuildServiceProvider();

        Assert.Equal(0, await TransferCommand.RunAsync(provider, reportPath));

        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("всё совпадает", report);
        Assert.Contains("Строк после запуска", report);
        Assert.Single(Paths.ExistingUserDatabases());
        Assert.True(File.Exists(Paths.LegacyKeptDatabase));
    }

    [Fact]
    public async Task CheckMigrations_OnCopy_OldColumnsUnchanged_NewColumnsListed()
    {
        // Папка данных с администратором, его файл — в схеме до миграции Mission (без миссии и «кем я хочу быть»), с данными
        await using (var factory = new PlannerApiFactory(_dataDirectory))
        {
            factory.CreateClient();
        }

        var file = Assert.Single(Paths.ExistingUserDatabases());
        TestDatabase.ClearPool(file);
        File.Delete(file);
        await using (var db = new PlannerDbContext(new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options))
        {
            await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db).MigrateAsync("StaleThoughts");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Работа', '#8fb4e3', 0, NULL);
                INSERT INTO Categories (Id, Name, Emoji, SortOrder, IsExpanded) VALUES (1, 'Книги', '📚', 0, 1);
                """);
        }

        var reportPath = Path.Combine(_dataDirectory, "сверка.md");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DataPaths.DataDirectoryKey] = _dataDirectory,
            [StorageInitializer.DailyBackupKey] = "false",
            [DataServiceCollectionExtensions.BackupPathKey] = Path.Combine(_dataDirectory, "backups"),
        }).Build();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration).AddPlannerData()
            .BuildServiceProvider();

        Assert.Equal(0, await TransferCommand.CheckMigrationsAsync(provider, reportPath));
        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("accounts.db", report);
        Assert.Contains("Roles.Vision", report);
        Assert.Contains("AppSettings.Mission", report);
        Assert.DoesNotContain("НЕ СОВПАЛО", report);
        Assert.Contains("| Roles | 1 | 1 | ✓ |", report);
    }

    [Fact]
    public async Task Fingerprint_SeesChangedValue()
    {
        await CreateLegacyDatabaseAsync();
        var copy = Path.Combine(_dataDirectory, "copy.db");
        File.Copy(Paths.LegacyDatabase, copy);
        await using (var connection = new SqliteConnection($"Data Source={copy};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Tasks SET Title = 'Толстая' WHERE Title = 'Толстой'";
            await command.ExecuteNonQueryAsync();
        }

        var differences = DatabaseFingerprint.Differences(
            await DatabaseFingerprint.ComputeAsync(Paths.LegacyDatabase), await DatabaseFingerprint.ComputeAsync(copy));

        Assert.Equal(["Tasks"], differences);
    }

    /// <summary>JSON без пробелов — для сравнения «до/после».</summary>
    private static string Normalize(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
