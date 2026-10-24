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

    public DbSet<LoginCode> LoginCodes => Set<LoginCode>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Invite> Invites => Set<Invite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
            user.HasIndex(u => u.Email).IsUnique();
            user.HasIndex(u => u.DeleteAfter);
        });

        modelBuilder.Entity<LoginCode>(code =>
        {
            code.ToTable("LoginCodes");
            code.HasKey(c => c.Id);
            code.Property(c => c.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
            code.Property(c => c.CodeHash).HasMaxLength(64);
            code.HasIndex(c => new { c.Email, c.CreatedAt });
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("Sessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Device).HasMaxLength(Session.DeviceMaxLength).IsRequired();
            session.Ignore(s => s.AbsoluteExpiresAt);
            session.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invite>(invite =>
        {
            invite.ToTable("Invites");
            invite.HasKey(i => i.Id);
            invite.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
            invite.HasIndex(i => i.TokenHash).IsUnique();
            invite.Property(i => i.Email).HasMaxLength(User.EmailMaxLength);
        });
    }
}
