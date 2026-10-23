using Microsoft.EntityFrameworkCore;
using Planner.Core.Accounts;

namespace Planner.Server.Data.Accounts;

/// <summary>
/// Общая база аккаунтов — accounts.db (docs/specs/accounts.md, D1). Данных планировщика здесь нет: они — в файле каждого пользователя.
/// Конфигурация — здесь, а не классами IEntityTypeConfiguration: PlannerDbContext подбирает такие классы из всей сборки.
/// </summary>
public class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
            user.HasIndex(u => u.Email).IsUnique();
        });
    }
}
