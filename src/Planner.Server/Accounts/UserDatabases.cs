using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;
using Planner.Server.Data;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Storage;

namespace Planner.Server.Accounts;

/// <summary>Что положить в новый файл данных: прежние начальные данные (первый администратор) или стартовый набор (N1).</summary>
public enum StarterSet
{
    Admin,
    NewUser,
}

/// <summary>Файлы данных пользователей (docs/specs/accounts.md, D3–D4): миграции; новый файл — с начальными данными.</summary>
public sealed class UserDatabases(DataPaths paths, TimeProvider clock)
{
    public async Task PrepareAsync(Guid userId, StarterSet starter = StarterSet.Admin, CancellationToken ct = default)
    {
        var path = paths.UserDatabase(userId);
        var isNew = !File.Exists(path);
        await using var db = PlannerDbContextFactory.Create(path, clock);
        await db.Database.MigrateAsync(ct);
        if (isNew && starter == StarterSet.NewUser)
        {
            await SeedData.CreateStarterSetAsync(db, ct);
        }
        else if (isNew)
        {
            await SeedData.EnsureSeededAsync(db, ct);
        }
    }
}

/// <summary>Новый пользователь: строка в accounts.db и свой файл данных.</summary>
public sealed class AccountService(AccountsDbContext accounts, UserDatabases databases, TimeProvider clock)
{
    public async Task<User> CreateUserAsync(string email, bool isAdmin, CancellationToken ct = default)
    {
        var user = User.Create(email, isAdmin, clock.GetUtcNow().UtcDateTime);
        await databases.PrepareAsync(user.Id, isAdmin ? StarterSet.Admin : StarterSet.NewUser, ct);
        accounts.Users.Add(user);
        await accounts.SaveChangesAsync(ct);
        return user;
    }
}
