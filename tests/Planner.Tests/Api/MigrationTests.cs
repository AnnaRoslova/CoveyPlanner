using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Planner.Server.Data;

namespace Planner.Tests.Api;

/// <summary>Обновление уже существующих баз (старые схемы и палитры) до текущей модели.</summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "planner-tests", $"{Guid.NewGuid():N}.db");

    private PlannerDbContext CreateContext()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new PlannerDbContext(new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite($"Data Source={_path}").Options);
    }

    [Fact]
    public async Task FromInitial_KeepsDataAndRecolorsRoles()
    {
        await using var db = CreateContext();

        // База в самой первой схеме с данными старого формата
        await db.GetService<IMigrator>().MigrateAsync("Initial");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Categories (Id, Name, SortOrder, IsExpanded, IsSystem) VALUES (1, 'Входящие', 0, 1, 1);
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Работа', '#6b8fb5', 0, NULL);
            INSERT INTO Settings (Id, DayStartHour, DayEndHour, SlotMinutes) VALUES (1, 7, 23, 30);
            INSERT INTO Tasks (Id, Title, CategoryId, State, PlannedDate, StartTime, DurationMinutes, SortOrder, CreatedAt)
                VALUES (1, 'Встреча', 1, 'Planned', '2026-09-28', '15:00:00', 60, 0, '2026-09-28 10:00:00');
            """);

        await db.Database.MigrateAsync();

        var category = await db.Categories.SingleAsync();
        Assert.Equal("Входящие", category.Name); // осталась обычной категорией
        Assert.Null(category.Emoji);

        // Цвет №1 первой палитры → №1 «Лотоса» (#f29ea6) → зефирный розовый (#efb8cc) → роза
        Assert.Equal("#f2b3c6", (await db.Roles.SingleAsync()).Color);

        var task = await db.Tasks.SingleAsync();
        Assert.Equal(new TimeOnly(15, 0), task.StartTime);
        Assert.Equal(new DateOnly(2026, 9, 28), task.PlannedDate);
    }

    /// <summary>Текущие стартовые цвета (как в SeedData): Тело, Ум, Дух, Общение, Я сама, Семья, Работа, Дом.</summary>
    private static readonly string[] CurrentStartColors =
        ["#9fd3bd", "#a9cde8", "#c9b8ec", "#f9d0e0", "#dcb8e6", "#b9d3b0", "#8fb4e3", "#f3e3a6"];

    [Fact]
    public async Task EighteenColorPalette_StartRolesGetNewStartColors_OthersNearestShade()
    {
        await using var db = CreateContext();

        // Стартовые роли с цветами «зелёного Лотоса» + свои роли
        await db.GetService<IMigrator>().MigrateAsync("GreenLotusPalette");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES
                (1, 'Тело', '#9fd3bd', 0, 'Body'),
                (2, 'Ум', '#94b4b5', 1, 'Mind'),
                (3, 'Дух', '#6fbfae', 2, 'Spirit'),
                (4, 'Общение и эмоции', '#efb8cc', 3, 'Social'),
                (5, 'Я сама', '#a8dcd0', 0, NULL),
                (6, 'Семья', '#b9d3b0', 1, NULL),
                (7, 'Работа', '#8ac2c4', 2, NULL),
                (8, 'Дом', '#7fc3b4', 3, NULL),
                (9, 'Друзья', '#e8bfd3', 4, NULL),
                (10, 'Спорт', '#94b4b5', 5, NULL),
                (11, 'Сад', '#8ac2c4', 6, NULL);
            """);

        await db.Database.MigrateAsync();

        var colors = await db.Roles.OrderBy(r => r.Id).Select(r => r.Color).ToListAsync();
        Assert.Equal(CurrentStartColors, colors.Take(8));
        Assert.Equal("#e8c1dc", colors[8]);  // цвета больше нет → ближайший (орхидея)
        Assert.Equal("#94b4b5", colors[9]);  // цвет остался в палитре — не трогаем
        Assert.Equal("#8ac2c4", colors[10]); // тот же цвет, что у прежней «Работы», но роль своя — не трогаем
    }

    [Fact]
    public async Task OlderPalettes_StartRolesEndWithCurrentStartColors()
    {
        await using var db = CreateContext();

        // Стартовые роли с цветами прошлой версии «Лотоса»
        await db.GetService<IMigrator>().MigrateAsync("RemoveGridAndInbox");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES
                (1, 'Тело', '#f4c3cf', 0, 'Body'),
                (2, 'Ум', '#94b4b5', 1, 'Mind'),
                (3, 'Дух', '#6fbfae', 2, 'Spirit'),
                (4, 'Общение и эмоции', '#f29ea6', 3, 'Social'),
                (5, 'Я сама', '#a8dcd0', 0, NULL),
                (6, 'Семья', '#e9b0b8', 1, NULL),
                (7, 'Работа', '#8ac2c4', 2, NULL),
                (8, 'Дом', '#f7d3db', 3, NULL);
            """);

        await db.Database.MigrateAsync();

        var colors = await db.Roles.OrderBy(r => r.Id).Select(r => r.Color).ToListAsync();
        Assert.Equal(CurrentStartColors, colors);
    }

    [Fact]
    public async Task SeedColors_AreStartColorsAndInPalette()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(db);

        var colors = await db.Roles.OrderBy(r => r.SawDimension == null).ThenBy(r => r.SortOrder).Select(r => r.Color).ToListAsync();
        Assert.Equal(CurrentStartColors, colors);
        Assert.All(colors, c => Assert.Contains(c, Planner.Core.Entities.RolePalette.Colors));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
        {
            File.Delete(file);
        }
    }
}
