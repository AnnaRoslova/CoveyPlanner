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

    [Fact]
    public async Task GoalSessions_LinkedTaskBecomesSession_DoneGoalKeepsProgress()
    {
        await using var db = CreateContext();

        // Старая схема: у цели одно дело (WeeklyGoals.TaskId) и своя отметка IsDone
        await db.GetService<IMigrator>().MigrateAsync("EighteenColorPalette");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Работа', '#8fb4e3', 0, NULL);
            INSERT INTO Tasks (Id, Title, RoleId, State, PlannedDate, SortOrder, CreatedAt, CompletedAt)
                VALUES (1, 'Отчёт', 1, 'Done', '2026-09-29', 0, '2026-09-28 10:00:00', '2026-09-29 10:00:00'),
                       (2, 'Звонок', 1, 'Planned', '2026-09-30', 0, '2026-09-28 10:00:00', NULL);
            INSERT INTO WeeklyGoals (Id, RoleId, WeekStart, Title, IsDone, SortOrder, TaskId) VALUES
                (1, 1, '2026-09-28', 'Отчёт', 1, 0, 1),
                (2, 1, '2026-09-28', 'Прочитать книгу', 1, 1, NULL),
                (3, 1, '2026-09-28', 'Выучить песню', 0, 2, NULL),
                (4, 1, '2026-09-28', 'Звонок', 0, 3, 2);
            """);

        await db.Database.MigrateAsync();

        var goals = await db.WeeklyGoals.Include(g => g.Sessions).OrderBy(g => g.Id).ToListAsync();
        Assert.All(goals, g => Assert.Equal(1, g.TimesPerWeek));
        Assert.Equal([1], goals[0].Sessions.Select(t => t.Id));
        Assert.True(goals[0].IsDone);

        // Отмеченная без дела цель — выполненное занятие без дня
        var mark = Assert.Single(goals[1].Sessions);
        Assert.Equal(Planner.Core.Enums.TaskState.Done, mark.State);
        Assert.Null(mark.PlannedDate);
        Assert.True(goals[1].IsDone);

        Assert.Empty(goals[2].Sessions);
        Assert.False(goals[2].IsDone);
        Assert.Equal([2], goals[3].Sessions.Select(t => t.Id));
        Assert.False(goals[3].IsDone);
    }

    [Fact]
    public async Task GoalTemplate_GoalTakesSessionQuadrant_AndSessionsFollowIt()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("GoalSessions");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Работа', '#8fb4e3', 0, NULL);
            INSERT INTO WeeklyGoals (Id, RoleId, WeekStart, Title, TimesPerWeek, SortOrder) VALUES
                (1, 1, '2026-09-28', 'Отчёт', 2, 0),
                (2, 1, '2026-09-28', 'Без квадранта', 1, 1);
            INSERT INTO Tasks (Id, Title, RoleId, State, PlannedDate, SortOrder, CreatedAt, Quadrant, GoalId) VALUES
                (1, 'Отчёт', 1, 'Planned', '2026-09-29', 0, '2026-09-28 10:00:00', NULL, 1),
                (2, 'Отчёт', 1, 'Planned', '2026-09-30', 0, '2026-09-28 10:00:00', 2, 1),
                (3, 'Отчёт', 1, 'Planned', '2026-10-01', 0, '2026-09-28 10:00:00', 3, 1),
                (4, 'Дело', 1, 'Planned', '2026-10-01', 1, '2026-09-28 10:00:00', 4, NULL);
            """);

        await db.Database.MigrateAsync();

        var goals = await db.WeeklyGoals.OrderBy(g => g.Id).ToListAsync();
        Assert.Equal(Planner.Core.Enums.Quadrant.NotUrgentImportant, goals[0].Quadrant);
        Assert.Null(goals[1].Quadrant);
        var quadrants = await db.Tasks.OrderBy(t => t.Id).Select(t => (int?)t.Quadrant).ToListAsync();
        Assert.Equal([2, 2, 2, 4], quadrants); // дело не цели не трогаем
    }

    [Fact]
    public async Task TaskRoles_RoleOfTaskMovesToTaskRoles_TasksKept()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("GoalTemplate");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Работа', '#8fb4e3', 0, NULL), (2, 'Дом', '#f3e3a6', 1, NULL);
            INSERT INTO Tasks (Id, Title, RoleId, State, PlannedDate, SortOrder, CreatedAt) VALUES
                (1, 'Отчёт', 1, 'Planned', '2026-09-29', 0, '2026-09-28 10:00:00'),
                (2, 'Полить цветы', 2, 'Done', '2026-09-30', 0, '2026-09-28 10:00:00'),
                (3, 'Без роли', NULL, 'Someday', NULL, 0, '2026-09-28 10:00:00');
            """);

        await db.Database.MigrateAsync();

        var tasks = await db.Tasks.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(3, tasks.Count); // пересборка таблицы Tasks ничего не удалила
        Assert.Equal([1], tasks[0].RoleIds);
        Assert.Equal([2], tasks[1].RoleIds);
        Assert.Empty(tasks[2].RoleIds);
    }

    [Fact]
    public async Task GoalSeries_SawGoals_BecomeSeries_RecurringOnlyIfRecent()
    {
        await using var db = CreateContext();
        var thisWeek = Planner.Core.Time.Week.StartOf(DateOnly.FromDateTime(DateTime.Now));
        string W(int offset) => thisWeek.AddDays(7 * offset).ToString("yyyy-MM-dd");

        await db.GetService<IMigrator>().MigrateAsync("TaskRoles");
        var sql = $"""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES
                (1, 'Тело', '#9fd3bd', 0, 'Body'), (2, 'Ум', '#a9cde8', 1, 'Mind'), (3, 'Работа', '#8fb4e3', 0, NULL);
            INSERT INTO WeeklyGoals (Id, RoleId, WeekStart, Title, TimesPerWeek, SortOrder, Notes) VALUES
                (1, 1, '{W(-3)}', 'Тренировка', 2, 0, NULL),
                (2, 1, '{W(-1)}', 'Тренировка', 3, 0, 'в зале'),
                (3, 2, '{W(0)}', 'Чтение', 1, 0, NULL),
                (4, 2, '{W(0)}', 'Чтение', 1, 1, NULL),
                (5, 2, '{W(-6)}', 'Курс', 1, 0, NULL),
                (6, 3, '{W(0)}', 'Отчёт', 1, 0, NULL);
            """;
        await db.Database.ExecuteSqlRawAsync(sql); // даты — свои, из теста

        await db.Database.MigrateAsync();

        var series = await db.GoalSeries.OrderBy(s => s.Title).ToListAsync();
        Assert.Equal(["Курс", "Тренировка", "Чтение"], series.Select(s => s.Title));

        var course = series[0];
        Assert.False(course.IsRecurring); // давняя — законченная серия
        Assert.Equal(DateOnly.Parse(W(-6)), course.LastWeek);

        var training = series[1];
        Assert.True(training.IsRecurring);
        Assert.Null(training.LastWeek);
        Assert.Equal((3, "в зале"), (training.TimesPerWeek, training.Notes)); // шаблон — с последней недели
        Assert.Equal(DateOnly.Parse(W(-3)), training.FirstWeek);
        Assert.Equal(DateOnly.Parse(W(-1)), training.GeneratedThrough);

        var goals = await db.WeeklyGoals.OrderBy(g => g.Id).ToListAsync();
        Assert.Equal([training.Id, training.Id, series[2].Id, null, course.Id, null], goals.Select(g => g.SeriesId));
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
