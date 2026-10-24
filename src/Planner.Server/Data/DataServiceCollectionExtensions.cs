using Microsoft.EntityFrameworkCore;
using Planner.Core.Abstractions;
using Planner.Core.Services;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Repositories;
using Planner.Server.Data.Storage;
using Planner.Server.Endpoints;

namespace Planner.Server.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Папка резервных копий; пусто — «backups» в папке данных.</summary>
    public const string BackupPathKey = "Storage:BackupPath";

    /// <summary>Сколько последних резервных копий хранить (по умолчанию 30).</summary>
    public const string BackupsToKeepKey = "Storage:BackupsToKeep";

    /// <summary>EF Core + SQLite (файл данных на пользователя и общая accounts.db), репозитории и сервисы Core.</summary>
    public static IServiceCollection AddPlannerData(this IServiceCollection services)
    {
        // Папка читается при первом обращении, а не при регистрации, чтобы тесты могли её переопределить
        services.AddSingleton(sp => DataPaths.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

        // Контекст данных — на файл того, чей запрос (docs/specs/accounts.md, D3); без пользователя не открывается
        services.AddDbContext<PlannerDbContext>((sp, options) => PlannerDbContextFactory.Configure(
            options,
            sp.GetRequiredService<DataPaths>().UserDatabase(sp.GetRequiredService<ICurrentUser>().UserId),
            sp.GetRequiredService<TimeProvider>()));
        services.AddDbContext<AccountsDbContext>((sp, options) =>
            options.UseSqlite(DataPaths.ConnectionString(sp.GetRequiredService<DataPaths>().AccountsDatabase)));
        services.AddScoped<StorageInitializer>();
        services.AddScoped<Planner.Server.Accounts.UserDatabases>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PlannerDbContext>());
        services.AddScoped<ITaskRepository, EfTaskRepository>();
        services.AddScoped<ICategoryRepository, EfCategoryRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IGoalRepository, EfGoalRepository>();
        services.AddScoped<IGoalSeriesRepository, EfGoalSeriesRepository>();
        services.AddScoped<IHabitRepository, EfHabitRepository>();
        services.AddScoped<IVacationRepository, EfVacationRepository>();
        services.AddScoped<ISettingsRepository, EfSettingsRepository>();

        services.AddScoped(sp => ResolveBackupOptions(sp.GetRequiredService<IConfiguration>()));
        services.AddScoped<BackupService>();

        // Часы сервера; в Development их можно сдвинуть (POST /api/dev/clock — для e2e «давно лежат»)
        services.AddSingleton<ShiftableTimeProvider>();
        services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<ShiftableTimeProvider>());
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
        services.AddScoped<SettingsService>();
        services.AddScoped<StaleThoughtService>();
        services.AddScoped<SearchService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<TaskViews>();

        return services;
    }

    public static BackupOptions ResolveBackupOptions(IConfiguration configuration)
    {
        var directory = configuration[BackupPathKey];
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(DataPaths.ResolveRoot(configuration), "backups");
        }

        var keep = configuration.GetValue(BackupsToKeepKey, 30);
        return new BackupOptions(Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory)), keep);
    }

    /// <summary>Подготовить хранилище при запуске (<see cref="StorageInitializer"/>); перенесён planner.db — отчёт.</summary>
    public static async Task<TransferReport?> InitializeStorageAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StorageInitializer>().InitializeAsync(ct);
    }
}
