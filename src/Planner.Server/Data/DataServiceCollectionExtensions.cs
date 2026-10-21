using Microsoft.EntityFrameworkCore;
using Planner.Core.Abstractions;
using Planner.Core.Services;
using Planner.Server.Data.Repositories;
using Planner.Server.Endpoints;

namespace Planner.Server.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Ключ настройки с путём к файлу БД (appsettings.json → "Storage:DatabasePath").</summary>
    public const string DatabasePathKey = "Storage:DatabasePath";

    /// <summary>Папка резервных копий; пусто — «backups» рядом с файлом БД.</summary>
    public const string BackupPathKey = "Storage:BackupPath";

    /// <summary>Сколько последних резервных копий хранить (по умолчанию 30).</summary>
    public const string BackupsToKeepKey = "Storage:BackupsToKeep";

    /// <summary>Делать ли копию «за сегодня» при запуске сервера (по умолчанию да).</summary>
    public const string DailyBackupKey = "Storage:DailyBackup";

    /// <summary>EF Core + SQLite, репозитории и сервисы Core.</summary>
    public static IServiceCollection AddPlannerData(this IServiceCollection services)
    {
        // Путь читается при создании контекста, чтобы тесты могли его переопределить
        services.AddDbContext<PlannerDbContext>((sp, options) =>
        {
            var path = ResolveDatabasePath(sp.GetRequiredService<IConfiguration>());
            options.UseSqlite($"Data Source={path}");
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PlannerDbContext>());
        services.AddScoped<ITaskRepository, EfTaskRepository>();
        services.AddScoped<ICategoryRepository, EfCategoryRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IGoalRepository, EfGoalRepository>();
        services.AddScoped<IGoalSeriesRepository, EfGoalSeriesRepository>();
        services.AddScoped<IHabitRepository, EfHabitRepository>();
        services.AddScoped<IVacationRepository, EfVacationRepository>();

        services.AddScoped(sp => ResolveBackupOptions(sp.GetRequiredService<IConfiguration>()));
        services.AddScoped<BackupService>();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TaskPlacer>();
        services.AddScoped<ProjectService>();
        services.AddScoped<GoalRemover>();
        services.AddScoped<GoalSessions>();
        services.AddScoped<GoalOrdering>();
        services.AddScoped<TaskGoalService>();
        services.AddScoped<GoalSeriesService>();
        services.AddScoped<TaskService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<RoleService>();
        services.AddScoped<GoalService>();
        services.AddScoped<HabitService>();
        services.AddScoped<StepService>();
        services.AddScoped<DoneArchiveService>();
        services.AddScoped<ReportService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<TaskViews>();

        return services;
    }

    /// <summary>Путь из настроек или по умолчанию %LOCALAPPDATA%\CoveyPlanner\planner.db. Папка создаётся.</summary>
    public static string ResolveDatabasePath(IConfiguration configuration)
    {
        var path = configuration[DatabasePathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            path = Path.Combine(appData, "CoveyPlanner", "planner.db");
        }

        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public static BackupOptions ResolveBackupOptions(IConfiguration configuration)
    {
        var directory = configuration[BackupPathKey];
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(Path.GetDirectoryName(ResolveDatabasePath(configuration))!, "backups");
        }

        var keep = configuration.GetValue(BackupsToKeepKey, 30);
        return new BackupOptions(Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory)), keep);
    }

    /// <summary>
    /// Подготовить базу при запуске: копия перед обновлением схемы (если база уже была),
    /// миграции, начальные данные, ежедневная копия.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var backups = scope.ServiceProvider.GetRequiredService<BackupService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Новые миграции для уже существующей базы — сначала копия, на случай если обновление что-то испортит
        var hasData = (await db.Database.GetAppliedMigrationsAsync()).Any();
        var hasPending = (await db.Database.GetPendingMigrationsAsync()).Any();
        if (hasData && hasPending)
        {
            await backups.CreateAsync("перед-обновлением");
        }

        await db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(db);

        if (configuration.GetValue(DailyBackupKey, true))
        {
            await backups.EnsureDailyAsync();
        }
    }
}
