using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Planner.Server.Data;

namespace Planner.Tests.Api;

/// <summary>Обновление уже существующей базы (схема Initial) до текущей модели.</summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "planner-tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task RemoveGridAndInbox_KeepsDataAndRecolorsRoles()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var db = new PlannerDbContext(
            new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite($"Data Source={_path}").Options);

        // База в старой схеме с данными старого формата
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

        Assert.Equal("#f29ea6", (await db.Roles.SingleAsync()).Color); // цвет №1 новой палитры

        var task = await db.Tasks.SingleAsync();
        Assert.Equal(new TimeOnly(15, 0), task.StartTime);
        Assert.Equal(new DateOnly(2026, 9, 28), task.PlannedDate);
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
