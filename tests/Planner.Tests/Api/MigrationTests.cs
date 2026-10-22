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

        var goals = await db.WeeklyGoals.Include(g => g.Links).ThenInclude(l => l.Task).OrderBy(g => g.Id).ToListAsync();
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

    [Fact]
    public async Task TaskGoals_GoalIdBecomesLink_NothingLost()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("GoalDays");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Тело', '#9fd3bd', 0, 'Body');
            INSERT INTO WeeklyGoals (Id, RoleId, WeekStart, Title, TimesPerWeek, SortOrder, AllowSeveralPerDay)
                VALUES (1, 1, '2026-09-28', 'Бег', 3, 0, 0);
            INSERT INTO Tasks (Id, Title, GoalId, State, PlannedDate, SortOrder, CreatedAt) VALUES
                (1, 'Бег', 1, 'Done', '2026-09-28', 0, '2026-09-28 10:00:00'),
                (2, 'Бег', 1, 'Planned', NULL, 0, '2026-09-28 10:00:00'),
                (3, 'Обычное дело', NULL, 'Planned', '2026-09-29', 0, '2026-09-28 10:00:00');
            """);

        await db.Database.MigrateAsync();

        var goal = await db.WeeklyGoals.Include(g => g.Links).ThenInclude(l => l.Task).SingleAsync();
        Assert.Equal([1, 2], goal.Sessions.Select(t => t.Id).Order());
        Assert.Equal(1, goal.DoneCount);
        var tasks = await db.Tasks.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal([1, 1, null], tasks.Select(t => t.GoalId));
    }

    [Fact]
    public async Task GoalRoles_RoleAndPlaceMoveToGoalRoles_SecondGoalLinksDropped()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("TaskGoals");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES
                (1, 'Тело', '#9fd3bd', 0, 'Body'), (2, 'Дух', '#c9b8ec', 2, 'Spirit');
            INSERT INTO GoalSeries (Id, RoleId, Title, TimesPerWeek, AllowSeveralPerDay, IsRecurring, FirstWeek, GeneratedThrough)
                VALUES (1, 2, 'Природа', 1, 0, 1, '2026-09-28', '2026-09-28');
            INSERT INTO WeeklyGoals (Id, RoleId, WeekStart, Title, TimesPerWeek, SortOrder, AllowSeveralPerDay, SeriesId) VALUES
                (1, 1, '2026-09-28', '10к шагов', 5, 0, 0, NULL),
                (2, 1, '2026-09-28', 'Бег', 3, 1, 0, NULL),
                (3, 2, '2026-09-28', 'Природа', 1, 0, 0, 1);
            INSERT INTO Tasks (Id, Title, State, PlannedDate, SortOrder, CreatedAt) VALUES
                (1, 'Прогулка в парке', 'Planned', '2026-09-28', 0, '2026-09-28 10:00:00'),
                (2, 'Бег', 'Done', '2026-09-29', 0, '2026-09-28 10:00:00');
            INSERT INTO TaskGoals (TaskId, GoalId, Position) VALUES (1, 1, 0), (1, 3, 1), (2, 2, 0);
            INSERT INTO TaskRoles (TaskId, RoleId) VALUES (1, 1), (1, 2), (2, 1);
            """);

        await db.Database.MigrateAsync();

        var goals = await db.WeeklyGoals.OrderBy(g => g.Id).ToListAsync();
        Assert.Equal([[1], [1], [2]], goals.Select(g => g.RoleIds.ToArray()));
        Assert.Equal([0, 1, 0], goals.Select(g => g.Roles.Single().SortOrder));
        Assert.Equal([2], (await db.GoalSeries.SingleAsync()).RoleIds);

        // «Прогулка» была и в «Природе» — осталась занятием только главной цели, роль — её
        var tasks = await db.Tasks.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal([1, 2], tasks.Select(t => t.GoalId));
        Assert.Equal([[1], [1]], tasks.Select(t => t.RoleIds.ToArray()));
        Assert.Equal(2, await db.Set<Planner.Core.Entities.TaskGoal>().CountAsync());
    }

    [Fact]
    public async Task Habits_TablesRebuilt_LinksAndRolesKept()
    {
        await using var db = CreateContext();

        // Внешние ключи на копилку SQLite добавляет пересборкой Tasks, WeeklyGoals и GoalSeries —
        // строки связанных таблиц (с каскадным удалением) должны уцелеть
        await db.GetService<IMigrator>().MigrateAsync("GoalRoles");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Тело', '#9fd3bd', 0, 'Body'), (2, 'Ум', '#a9cde8', 1, 'Mind');
            INSERT INTO GoalSeries (Id, Title, TimesPerWeek, AllowSeveralPerDay, IsRecurring, FirstWeek, GeneratedThrough)
                VALUES (1, 'Бег', 3, 0, 1, '2026-09-28', '2026-09-28');
            INSERT INTO GoalSeriesRoles (SeriesId, RoleId) VALUES (1, 1), (1, 2);
            INSERT INTO WeeklyGoals (Id, WeekStart, Title, TimesPerWeek, AllowSeveralPerDay, SeriesId)
                VALUES (1, '2026-09-28', 'Бег', 3, 0, 1);
            INSERT INTO GoalRoles (GoalId, RoleId, SortOrder) VALUES (1, 1, 0), (1, 2, 3);
            INSERT INTO Tasks (Id, Title, State, PlannedDate, SortOrder, CreatedAt, CompletedAt) VALUES
                (1, 'Бег', 'Done', '2026-09-28', 0, '2026-09-28 10:00:00', '2026-09-28 11:00:00'),
                (2, 'Книга', 'Someday', NULL, 0, '2026-09-28 10:00:00', NULL);
            INSERT INTO TaskGoals (TaskId, GoalId, Position) VALUES (1, 1, 0);
            INSERT INTO TaskRoles (TaskId, RoleId) VALUES (1, 1), (1, 2), (2, 2);
            """);

        await db.Database.MigrateAsync();

        async Task<long> Count(string table)
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            return (long)(await command.ExecuteScalarAsync())!;
        }

        long[] expected = [2, 1, 1, 1, 2, 2, 3];
        var counts = new List<long>();
        foreach (var table in new[] { "Tasks", "WeeklyGoals", "GoalSeries", "TaskGoals", "GoalRoles", "GoalSeriesRoles", "TaskRoles" })
        {
            counts.Add(await Count(table));
        }

        Assert.Equal(expected, counts);
        var goal = await db.WeeklyGoals.Include(g => g.Links).ThenInclude(l => l.Task).SingleAsync();
        Assert.Equal(1, goal.DoneCount);
    }

    [Fact]
    public async Task HabitsAreSeries_HabitsAndCollectionsGone_TasksGoalsSeriesCategoriesKept()
    {
        await using var db = CreateContext();

        // Копилки (привычка и коллекция) с привязками к серии, цели, делу и категории; каникулы — копилки и «для всех».
        // Снятие колонок SQLite делает пересборкой Tasks, WeeklyGoals и GoalSeries — связанные строки должны уцелеть
        await db.GetService<IMigrator>().MigrateAsync("Collections");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Тело', '#9fd3bd', 0, 'Body');
            INSERT INTO Categories (Id, Name, SortOrder, IsExpanded) VALUES (5, 'Проекты', 0, 1);
            INSERT INTO Habits (Id, Name, Color, WeeklyNorm, JarSize, StartCount, CreatedAt, SortOrder, IsArchived, Kind)
                VALUES (1, 'Бег', '#9fd3bd', 3, 30, 0, '2026-10-05 10:00:00', 0, 0, 'Habit'),
                       (4, 'Проекты', '#cfe3a8', 1, 30, 1, '2026-10-05 10:00:00', 1, 0, 'Collection');
            INSERT INTO HabitCategories (HabitId, CategoryId) VALUES (4, 5);
            INSERT INTO GoalSeries (Id, Title, TimesPerWeek, AllowSeveralPerDay, IsRecurring, FirstWeek, GeneratedThrough, HabitId)
                VALUES (1, 'Бег', 3, 0, 1, '2026-09-28', '2026-10-05', 1);
            INSERT INTO GoalSeriesRoles (SeriesId, RoleId) VALUES (1, 1);
            INSERT INTO WeeklyGoals (Id, WeekStart, Title, TimesPerWeek, AllowSeveralPerDay, SeriesId, HabitId)
                VALUES (1, '2026-09-28', 'Бег', 3, 0, 1, NULL), (2, '2026-10-05', 'Бег', 3, 0, 1, 1);
            INSERT INTO GoalRoles (GoalId, RoleId, SortOrder) VALUES (1, 1, 0), (2, 1, 0);
            INSERT INTO Tasks (Id, Title, State, PlannedDate, SortOrder, CreatedAt, CompletedAt, HabitId, CategoryId) VALUES
                (1, 'Бег', 'Done', '2026-09-28', 0, '2026-09-28 10:00:00', '2026-09-28 11:00:00', NULL, NULL),
                (2, 'Бег', 'Done', '2026-10-05', 0, '2026-10-05 10:00:00', '2026-10-05 11:00:00', 1, NULL),
                (3, 'Починить', 'Planned', '2026-10-06', 1, '2026-10-05 10:00:00', NULL, 4, NULL),
                (4, 'Маркетплейс', 'Someday', NULL, 0, '2026-10-05 10:00:00', NULL, NULL, 5);
            INSERT INTO TaskGoals (TaskId, GoalId, Position) VALUES (1, 1, 0), (2, 2, 0);
            INSERT INTO TaskRoles (TaskId, RoleId) VALUES (1, 1), (2, 1);
            INSERT INTO Vacations (Id, "From", "To") VALUES (1, '2026-10-01', '2026-10-03'), (2, '2026-10-10', '2026-10-12');
            INSERT INTO VacationHabits (VacationId, HabitId) VALUES (1, 1);
            """);

        await db.Database.MigrateAsync();

        long[] expected = [4, 2, 1, 2, 2, 1, 2, 1, 1, 0];
        var counts = new List<long>();
        foreach (var table in new[]
                 { "Tasks", "WeeklyGoals", "GoalSeries", "TaskGoals", "GoalRoles", "GoalSeriesRoles", "TaskRoles", "Categories", "Vacations", "VacationHabits" })
        {
            counts.Add(await CountAsync(db, table));
        }

        Assert.Equal(expected, counts);
        Assert.Equal(0, await CountAsync(db, "sqlite_master WHERE name IN ('Habits', 'HabitCategories')"));

        // Серия — привычка с банкой по умолчанию; каникулы «для всех» остались, каникулы копилки — нет
        var series = await db.GoalSeries.SingleAsync();
        Assert.Equal((30, 0, true), (series.JarSize, series.StartCount, series.IsHabit));
        Assert.Equal(2, (await db.Vacations.SingleAsync()).Id);
        var goals = await db.WeeklyGoals.Include(g => g.Links).ThenInclude(l => l.Task).OrderBy(g => g.Id).ToListAsync();
        Assert.Equal([1, 1], goals.Select(g => g.DoneCount));
        Assert.Equal(5, (await db.Tasks.SingleAsync(t => t.Id == 4)).CategoryId);
    }

    [Fact]
    public async Task TasksStepsDone_HabitFlag_FromSomeday_StepOrder()
    {
        await using var db = CreateContext();

        // Как у Анны: постоянные серии пилы, выполненные дела из категорий в днях, мысли ленты, корзина; плюс крайние случаи
        await db.GetService<IMigrator>().MigrateAsync("HabitsAreSeries");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Тело', '#9fd3bd', 0, 'Body'), (5, 'Работа', '#8fb4e3', 4, NULL);
            INSERT INTO Categories (Id, Name, SortOrder, IsExpanded) VALUES (3, 'Планер', 0, 1);
            INSERT INTO GoalSeries (Id, Title, TimesPerWeek, AllowSeveralPerDay, IsRecurring, FirstWeek, GeneratedThrough, JarSize, StartCount, IsHabitDeleted)
                VALUES (1, 'Бег', 3, 0, 1, '2026-09-28', '2026-10-05', 30, 0, 0),
                       (2, 'Йога', 2, 0, 1, '2026-09-28', '2026-10-05', 30, 0, 1),
                       (3, 'Отчёт', 1, 0, 1, '2026-09-28', '2026-10-05', 30, 0, 0),
                       (4, 'Прогулка', 1, 0, 0, '2026-09-28', '2026-09-28', 30, 0, 0);
            INSERT INTO GoalSeriesRoles (SeriesId, RoleId) VALUES (1, 1), (2, 1), (3, 5), (4, 1);
            INSERT INTO Tasks (Id, Title, State, PreviousState, PlannedDate, SortOrder, CreatedAt, CompletedAt, CategoryId, ParentId) VALUES
                (1, 'Мысль', 'Someday', NULL, NULL, 0, '2026-10-01', NULL, 3, NULL),
                (2, 'Неразобранное', 'Someday', NULL, NULL, 0, '2026-10-01', NULL, NULL, NULL),
                (3, 'Сделано из ленты', 'Done', NULL, '2026-10-01', 0, '2026-10-01', '2026-10-01', 3, NULL),
                (4, 'Дело недели', 'Done', NULL, '2026-10-01', 1, '2026-10-01', '2026-10-01', NULL, NULL),
                (5, 'В корзине из ленты', 'Trashed', 'Someday', NULL, 0, '2026-10-01', NULL, NULL, NULL),
                (6, 'В корзине из плана', 'Trashed', 'Planned', '2026-10-01', 0, '2026-10-01', NULL, NULL, NULL),
                (7, 'Шаг', 'Planned', NULL, NULL, 3, '2026-10-01', NULL, NULL, 4);
            """);

        await db.Database.MigrateAsync();

        var habits = await db.GoalSeries.OrderBy(s => s.Id).Select(s => s.IsHabit).ToListAsync();
        Assert.Equal([true, false, false, false], habits);

        var tasks = await db.Tasks.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal([1, 2, 3, 5], tasks.Where(t => t.IsFromSomeday).Select(t => t.Id));
        Assert.Equal(3, tasks.Single(t => t.Id == 7).StepOrder);
        Assert.All(tasks.Where(t => t.Id != 7), t => Assert.Null(t.StepOrder));
        Assert.All(tasks, t => Assert.Null(t.ClipSeriesId));
    }

    [Fact]
    public async Task Projects_FlagOnTasksWithSteps_AndGoalProjects()
    {
        await using var db = CreateContext();

        // Мысль; проект ленты с шагом; проект в дне только с шагом в корзине; проект цели без шагов; шаг сам проектом не становится
        await db.GetService<IMigrator>().MigrateAsync("TasksStepsDone");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Categories (Id, Name, SortOrder, IsExpanded) VALUES (3, 'Дом', 0, 1);
            INSERT INTO Tasks (Id, Title, State, PreviousState, PlannedDate, SortOrder, CreatedAt, CategoryId, ParentId, IsFromSomeday) VALUES
                (1, 'Мысль', 'Someday', NULL, NULL, 0, '2026-10-01', 3, NULL, 1),
                (2, 'Ремонт', 'Someday', NULL, NULL, 1, '2026-10-01', 3, NULL, 1),
                (3, 'Стены', 'Planned', NULL, NULL, 0, '2026-10-01', NULL, 2, 0),
                (4, 'Переезд', 'Planned', NULL, '2026-10-02', 0, '2026-10-01', NULL, NULL, 0),
                (5, 'Коробки', 'Trashed', 'Planned', NULL, 0, '2026-10-01', NULL, 4, 0),
                (6, 'Атомные привычки', 'Someday', NULL, NULL, 2, '2026-10-01', 3, NULL, 1);
            INSERT INTO GoalSeries (Id, Title, TimesPerWeek, AllowSeveralPerDay, IsRecurring, FirstWeek, GeneratedThrough, JarSize, StartCount, IsHabit, ProjectId)
                VALUES (1, 'Чтение', 3, 0, 1, '2026-09-28', '2026-10-05', 30, 0, 1, 6);
            """);

        await db.Database.MigrateAsync();

        var projects = await db.Tasks.Where(t => t.IsProject).OrderBy(t => t.Id).Select(t => t.Id).ToListAsync();
        Assert.Equal([2, 4, 6], projects);
        Assert.Equal(3, (await db.Tasks.SingleAsync(t => t.Id == 2)).CategoryId); // категория остаётся
    }

    [Fact]
    public async Task WeekWithoutDay_NewEmptyColumn_DataKept()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("Projects");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tasks (Id, Title, State, PlannedDate, SortOrder, CreatedAt, IsFromSomeday, IsProject) VALUES
                (1, 'Мысль', 'Someday', NULL, 0, '2026-10-01', 1, 0),
                (2, 'Звонок', 'Planned', '2026-10-14', 0, '2026-10-01', 0, 0);
            """);

        await db.Database.MigrateAsync();

        var tasks = await db.Tasks.OrderBy(t => t.Id).ToListAsync();
        Assert.All(tasks, t => Assert.Null(t.PlannedWeek));
        Assert.Equal(new DateOnly(2026, 10, 14), tasks[1].PlannedDate);
    }

    /// <summary>batch-ux.md BM: миссия и «кем я хочу быть» — пустые, блок в панели выключен; роли, их порядок и цвета не меняются.</summary>
    [Fact]
    public async Task Mission_EmptyFields_RolesKept()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("StaleThoughts");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (10, 'Работа', '#8fb4e3', 2, NULL), (11, 'Дом', '#f3e3a6', 3, NULL);
            """);

        await db.Database.MigrateAsync();

        var roles = await db.Roles.Where(r => r.Id >= 10).OrderBy(r => r.Id).ToListAsync();
        Assert.Equal([("Работа", "#8fb4e3", 2), ("Дом", "#f3e3a6", 3)], roles.Select(r => (r.Name, r.Color, r.SortOrder)).ToList());
        Assert.All(roles, r => Assert.Null(r.Vision));
        var settings = await db.AppSettings.SingleAsync();
        Assert.Equal((3, (string?)null, false), (settings.StaleMonths, settings.Mission, settings.ShowMissionInPanel));
    }

    /// <summary>
    /// batch-ux.md BS4a: UpdatedAt = CreatedAt (возраст честный); мыслям, уже старым при установке, — StaleNotifiedAt (плашек о них нет);
    /// свежая мысль, проект и дело плана не помечены; срок в AppSettings — 3 мес.
    /// </summary>
    [Fact]
    public async Task StaleThoughts_AgeFromCreation_AlreadyStaleThoughtsGetNoNotice()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("RestoreStoppedSeriesWeeks");
        var old = DateTime.UtcNow.AddMonths(-5).ToString("yyyy-MM-dd HH:mm:ss.fffffff");
        var recent = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM-dd HH:mm:ss.fffffff");
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO Tasks (Id, Title, State, SortOrder, CreatedAt, IsFromSomeday, IsProject) VALUES
                (1, 'Сад на балконе', 'Someday', 0, {old}, 1, 0),
                (2, 'Свежая мысль', 'Someday', 1, {recent}, 1, 0),
                (3, 'Ремонт', 'Someday', 2, {old}, 1, 1),
                (4, 'Звонок', 'Planned', 0, {old}, 0, 0);
            """);

        await db.Database.MigrateAsync();

        var tasks = await db.Tasks.OrderBy(t => t.Id).ToListAsync();
        Assert.All(tasks, t => Assert.Equal(t.CreatedAt, t.UpdatedAt));
        Assert.Equal([true, false, false, false], tasks.Select(t => t.StaleNotifiedAt is not null).ToArray());
        Assert.All(tasks, t => Assert.Null(t.KeptAt));
        Assert.Equal(3, (await db.AppSettings.SingleAsync()).StaleMonths);
    }

    /// <summary>
    /// fixes-2026-10.md A3: авто-проект привычки (название серии, только занятия и удалённый шаг) — отвязан и в корзине;
    /// книга с другим названием, проект с настоящим шагом и проект разовой цели — не тронуты.
    /// </summary>
    [Fact]
    public async Task HabitsAreNotProjects_AutoProjectOfRecurringSeries_UnlinkedAndTrashed_OthersKept()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("WeekWithoutDay");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Ум', '#a9cde8', 0, 'Mind');
            INSERT INTO Tasks (Id, Title, State, PreviousState, PlannedDate, SortOrder, CreatedAt, ParentId, StepOrder, IsFromSomeday, IsProject) VALUES
                (1, 'Чтение', 'Someday', NULL, NULL, 0, '2026-10-01', NULL, NULL, 1, 1),
                (2, 'Чтение', 'Done', NULL, '2026-10-08', 0, '2026-10-01', 1, 0, 0, 0),
                (3, 'Чтение', 'Planned', NULL, '2026-10-16', 0, '2026-10-01', 1, 1, 0, 0),
                (4, 'пвпа', 'Trashed', 'Planned', NULL, 0, '2026-10-01', 1, 2, 0, 0),
                (5, 'Атомные привычки', 'Someday', NULL, NULL, 1, '2026-10-01', NULL, NULL, 1, 1),
                (6, 'Бег', 'Someday', NULL, NULL, 2, '2026-10-01', NULL, NULL, 1, 1),
                (7, 'Кроссовки', 'Planned', NULL, NULL, 0, '2026-10-01', 6, 0, 0, 0),
                (8, 'Собес', 'Someday', NULL, NULL, 3, '2026-10-01', NULL, NULL, 1, 1);
            INSERT INTO GoalSeries (Id, Title, TimesPerWeek, AllowSeveralPerDay, IsRecurring, FirstWeek, GeneratedThrough, JarSize, StartCount, IsHabit, ProjectId) VALUES
                (1, 'Чтение', 3, 0, 1, '2026-10-05', '2026-10-19', 30, 0, 1, 1),
                (2, 'Чтение книги', 3, 0, 1, '2026-10-05', '2026-10-12', 30, 0, 1, 5),
                (3, 'Бег', 2, 0, 1, '2026-10-05', '2026-10-12', 30, 0, 1, 6),
                (4, 'Собес', 1, 0, 0, '2026-10-12', '2026-10-12', 30, 0, 0, 8);
            INSERT INTO WeeklyGoals (Id, WeekStart, Title, TimesPerWeek, AllowSeveralPerDay, SeriesId, ProjectId) VALUES
                (1, '2026-10-05', 'Чтение', 3, 0, 1, 1),
                (2, '2026-10-12', 'Чтение', 3, 0, 1, 1),
                (3, '2026-10-19', 'Чтение', 3, 0, 1, 1),
                (4, '2026-10-12', 'Собес', 1, 0, 4, 8);
            INSERT INTO GoalRoles (GoalId, RoleId, SortOrder) VALUES (1, 1, 0), (2, 1, 0), (3, 1, 0), (4, 1, 1);
            INSERT INTO TaskGoals (TaskId, GoalId, Position) VALUES (2, 1, 0), (3, 2, 0);
            """);

        await db.Database.MigrateAsync();

        var tasks = await db.Tasks.OrderBy(t => t.Id).ToDictionaryAsync(t => t.Id);
        Assert.Equal(Planner.Core.Enums.TaskState.Trashed, tasks[1].State);
        Assert.Equal(Planner.Core.Enums.TaskState.Someday, tasks[1].PreviousState);
        Assert.NotNull(tasks[1].TrashedAt);
        Assert.All([tasks[2], tasks[3]], t => Assert.Null(t.ParentId)); // занятия отвязаны, остались в днях
        Assert.Equal(new DateOnly(2026, 10, 8), tasks[2].PlannedDate);
        Assert.Equal(Planner.Core.Enums.TaskState.Done, tasks[2].State);
        Assert.Equal(1, tasks[4].ParentId); // удалённый шаг — при проекте в корзине
        Assert.Equal([5, 6, 8], tasks.Values.Where(t => t.IsProject && t.State != Planner.Core.Enums.TaskState.Trashed).Select(t => t.Id));
        Assert.Equal(6, tasks[7].ParentId);

        var series = await db.GoalSeries.OrderBy(s => s.Id).Select(s => s.ProjectId).ToListAsync();
        Assert.Equal([null, 5, 6, 8], series);
        var goals = await db.WeeklyGoals.OrderBy(g => g.Id).Select(g => g.ProjectId).ToListAsync();
        Assert.Equal([null, null, null, 8], goals);
        Assert.Equal(2, await CountAsync(db, "TaskGoals")); // занятия — по-прежнему занятия
    }

    /// <summary>
    /// fixes-2026-10.md E1.3: у «Тренировки со скакалкой» — цели 12, 19, 26 окт по шаблону серии, в конец списка роли, без занятий;
    /// существующие недели не тронуты; у законченной серии GeneratedThrough = LastWeek; другие серии — без изменений.
    /// </summary>
    [Fact]
    public async Task RestoreStoppedSeriesWeeks_JumpRopeGapFilled_EndedSeriesReset()
    {
        await using var db = CreateContext();

        await db.GetService<IMigrator>().MigrateAsync("HabitsAreNotProjects");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Roles (Id, Name, Color, SortOrder, SawDimension) VALUES (1, 'Тело', '#9fd3bd', 0, 'Body');
            INSERT INTO GoalSeries (Id, Title, TimesPerWeek, AllowSeveralPerDay, IsRecurring, FirstWeek, LastWeek, GeneratedThrough, JarSize, StartCount, IsHabit, Quadrant, Days) VALUES
                (6, 'Тренировка со скакалкой', 7, 0, 1, '2026-09-28', NULL, '2026-11-09', 30, 0, 1, 2, NULL),
                (3, 'Общаться с Алиной', 3, 1, 1, '2026-09-28', '2026-09-28', '2026-10-26', 30, 0, 1, 2, NULL),
                (5, '10к шагов', 6, 0, 1, '2026-09-28', NULL, '2026-11-09', 30, 0, 1, 2, 63);
            INSERT INTO GoalSeriesRoles (SeriesId, RoleId) VALUES (6, 1), (3, 1), (5, 1);
            INSERT INTO WeeklyGoals (Id, WeekStart, Title, TimesPerWeek, AllowSeveralPerDay, SeriesId, Quadrant) VALUES
                (16, '2026-10-05', 'Тренировка со скакалкой', 7, 0, 6, 2),
                (55, '2026-11-02', 'Тренировка со скакалкой', 7, 0, 6, 2),
                (23, '2026-10-12', '10к шагов', 6, 0, 5, 2);
            INSERT INTO GoalRoles (GoalId, RoleId, SortOrder) VALUES (16, 1, 1), (55, 1, 2), (23, 1, 4);
            """);

        await db.Database.MigrateAsync();

        var jumpRope = await db.WeeklyGoals.Where(g => g.SeriesId == 6).OrderBy(g => g.WeekStart).ToListAsync();
        Assert.Equal(
            ["2026-10-05", "2026-10-12", "2026-10-19", "2026-10-26", "2026-11-02"],
            jumpRope.Select(g => g.WeekStart.ToString("yyyy-MM-dd")));
        var restored = jumpRope.Where(g => g.Id is not (16 or 55)).ToList();
        Assert.All(restored, g => Assert.Equal((7, Planner.Core.Enums.Quadrant.NotUrgentImportant, "Тренировка со скакалкой"), (g.TimesPerWeek, g.Quadrant!.Value, g.Title)));
        Assert.Equal([5, 0, 0], restored.Select(g => g.RoleLink(1)!.SortOrder)); // после «10к шагов» (4) — 5; на пустых неделях — 0
        Assert.Equal(0, await CountAsync(db, "TaskGoals"));

        var series = await db.GoalSeries.OrderBy(s => s.Id).ToDictionaryAsync(s => s.Id);
        Assert.Equal(new DateOnly(2026, 9, 28), series[3].GeneratedThrough);
        Assert.Equal(new DateOnly(2026, 11, 9), series[6].GeneratedThrough);
        Assert.Equal(1, await db.WeeklyGoals.CountAsync(g => g.SeriesId == 5)); // у серии с выбранными днями — без изменений
    }

    private static async Task<long> CountAsync(PlannerDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public void Dispose()
    {
        TestDatabase.ClearPool(_path);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
        {
            File.Delete(file);
        }
    }
}
