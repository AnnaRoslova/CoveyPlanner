using Microsoft.EntityFrameworkCore;
using Planner.Core.Abstractions;
using Planner.Core.Services;
using Planner.Server.Data.Repositories;

namespace Planner.Server.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Ключ настройки с путём к файлу БД (appsettings.json → "Storage:DatabasePath").</summary>
    public const string DatabasePathKey = "Storage:DatabasePath";

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
        services.AddScoped<ISettingsRepository, EfSettingsRepository>();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TaskPlacer>();
        services.AddScoped<TaskService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<RoleService>();
        services.AddScoped<GoalService>();

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

    /// <summary>Применить миграции и заполнить начальные данные.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        await db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(db);
    }
}
