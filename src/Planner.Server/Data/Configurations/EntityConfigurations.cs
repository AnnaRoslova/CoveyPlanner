using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planner.Core.Entities;

namespace Planner.Server.Data.Configurations;

// Перечисления храним строками — так файл БД читается глазами при бэкапе и отладке.

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.Property(t => t.Title).IsRequired().HasMaxLength(TextRules.TitleMaxLength);
        builder.Property(t => t.Notes).HasMaxLength(TextRules.NotesMaxLength);
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.PreviousState).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Quadrant).HasConversion<int?>();

        // Удалили категорию — у дел ссылка просто обнуляется
        builder.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);

        // Роли дела — таблица TaskRoles (TaskId, RoleId); удалили дело или роль — строка удаляется
        builder.OwnsMany(t => t.Roles, roles =>
        {
            roles.ToTable("TaskRoles");
            roles.WithOwner().HasForeignKey("TaskId");
            roles.HasKey("TaskId", nameof(TaskRole.RoleId));
            roles.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Navigation(t => t.Roles).HasField("_roles");
        builder.Ignore(t => t.RoleIds);

        // Цель дела (TaskGoals) всегда загружается вместе с делом — по ней видно, «камень» ли это
        builder.Navigation(t => t.GoalLinks).AutoInclude();
        builder.Ignore(t => t.GoalLink);
        builder.Ignore(t => t.GoalId);
        builder.Ignore(t => t.IsSession);

        builder.HasIndex(t => new { t.State, t.CategoryId, t.SortOrder });
        builder.HasIndex(t => new { t.State, t.PlannedDate });
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).IsRequired().HasMaxLength(TextRules.NameMaxLength);
        builder.Property(c => c.Emoji).HasMaxLength(Category.EmojiMaxLength);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(r => r.Name).IsRequired().HasMaxLength(TextRules.NameMaxLength);
        builder.Property(r => r.Color).IsRequired().HasMaxLength(7);
        builder.Property(r => r.SawDimension).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(r => r.SawDimension).IsUnique();
    }
}

public class WeeklyGoalConfiguration : IEntityTypeConfiguration<WeeklyGoal>
{
    public void Configure(EntityTypeBuilder<WeeklyGoal> builder)
    {
        builder.Property(g => g.Title).IsRequired().HasMaxLength(TextRules.TitleMaxLength);
        builder.Property(g => g.Notes).HasMaxLength(TextRules.NotesMaxLength);
        builder.Property(g => g.Quadrant).HasConversion<int?>();
        builder.Property(g => g.Days).HasConversion<int?>(); // набор дней — битовая маска

        // Роли цели — таблица GoalRoles (GoalId, RoleId, SortOrder — место цели в списке роли);
        // удалили цель или роль — строка удаляется (что делать с целью без ролей, решает RoleService)
        builder.OwnsMany(g => g.Roles, roles =>
        {
            roles.ToTable("GoalRoles");
            roles.WithOwner().HasForeignKey("GoalId");
            roles.HasKey("GoalId", nameof(GoalRole.RoleId));
            roles.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
            roles.HasIndex(r => r.RoleId);
        });
        builder.Navigation(g => g.Roles).HasField("_roles");
        builder.Ignore(g => g.RoleIds);

        // Занятия цели — дела, связанные с ней через TaskGoals (см. TaskGoalConfiguration)
        builder.Ignore(g => g.Sessions);
        builder.Ignore(g => g.ActiveSessions);
        builder.Ignore(g => g.UnscheduledSession);

        builder.HasIndex(g => g.WeekStart);

        // Серия: удалили серию (у неё не осталось ролей) — цели остаются разовыми; на неделе у серии одна цель
        builder.HasOne(g => g.Series).WithMany().HasForeignKey(g => g.SeriesId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(g => new { g.SeriesId, g.WeekStart }).IsUnique();
    }
}

public class GoalSeriesConfiguration : IEntityTypeConfiguration<GoalSeries>
{
    public void Configure(EntityTypeBuilder<GoalSeries> builder)
    {
        builder.Property(s => s.Title).IsRequired().HasMaxLength(TextRules.TitleMaxLength);
        builder.Property(s => s.Notes).HasMaxLength(TextRules.NotesMaxLength);
        builder.Property(s => s.Quadrant).HasConversion<int?>();
        builder.Property(s => s.Days).HasConversion<int?>();

        // Роли шаблона серии — таблица GoalSeriesRoles (SeriesId, RoleId)
        builder.OwnsMany(s => s.Roles, roles =>
        {
            roles.ToTable("GoalSeriesRoles");
            roles.WithOwner().HasForeignKey("SeriesId");
            roles.HasKey("SeriesId", nameof(SeriesRole.RoleId));
            roles.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
            roles.HasIndex(r => r.RoleId);
        });
        builder.Navigation(s => s.Roles).HasField("_roles");
        builder.Ignore(s => s.RoleIds);
        builder.HasIndex(s => s.IsRecurring);
    }
}

/// <summary>
/// Связь дела с целью (TaskGoals): у дела — не больше одной цели.
/// Удалили дело или цель — связь удаляется, само дело остаётся.
/// </summary>
public class TaskGoalConfiguration : IEntityTypeConfiguration<TaskGoal>
{
    public void Configure(EntityTypeBuilder<TaskGoal> builder)
    {
        builder.ToTable("TaskGoals");
        builder.HasKey(l => new { l.TaskId, l.GoalId });
        builder.HasOne(l => l.Task).WithMany(t => t.GoalLinks).HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Goal).WithMany(g => g.Links).HasForeignKey(l => l.GoalId).OnDelete(DeleteBehavior.Cascade);
    }
}
