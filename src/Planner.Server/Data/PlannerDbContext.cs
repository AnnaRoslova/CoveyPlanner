using Microsoft.EntityFrameworkCore;
using Planner.Core.Abstractions;
using Planner.Core.Entities;

namespace Planner.Server.Data;

public class PlannerDbContext(DbContextOptions<PlannerDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<WeeklyGoal> WeeklyGoals => Set<WeeklyGoal>();

    public DbSet<UserSettings> Settings => Set<UserSettings>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => await SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlannerDbContext).Assembly);
}
