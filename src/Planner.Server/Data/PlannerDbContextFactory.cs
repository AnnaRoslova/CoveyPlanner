using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Storage;

namespace Planner.Server.Data;

/// <summary>Контекст данных на один файл пользователя (docs/specs/accounts.md, D3) — и в запросах, и при запуске (миграции).</summary>
public static class PlannerDbContextFactory
{
    public static void Configure(DbContextOptionsBuilder options, string databasePath, TimeProvider clock)
    {
        options.UseSqlite(DataPaths.ConnectionString(databasePath));
        options.AddInterceptors(new TaskTouchInterceptor(clock));
    }

    public static PlannerDbContext Create(string databasePath, TimeProvider clock)
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>();
        Configure(options, databasePath, clock);
        return new PlannerDbContext(options.Options);
    }
}

/// <summary>Для dotnet ef migrations: в запросе контекст открывается на файл пользователя, а у инструментов пользователя нет.</summary>
public sealed class PlannerDesignTimeFactory : IDesignTimeDbContextFactory<PlannerDbContext>
{
    public PlannerDbContext CreateDbContext(string[] args) => PlannerDbContextFactory.Create("design-time.db", TimeProvider.System);
}

public sealed class AccountsDesignTimeFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseSqlite("Data Source=design-time-accounts.db").Options);
}
