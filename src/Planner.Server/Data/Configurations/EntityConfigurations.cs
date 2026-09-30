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

        // Удалили категорию или роль — у дел ссылка просто обнуляется
        builder.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Role>().WithMany().HasForeignKey(t => t.RoleId).OnDelete(DeleteBehavior.SetNull);

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

        builder.HasOne<Role>().WithMany().HasForeignKey(g => g.RoleId).OnDelete(DeleteBehavior.Cascade);
        // Занятия цели — обычные дела; удалили цель — у дел ссылка обнуляется
        builder.HasMany(g => g.Sessions).WithOne().HasForeignKey(t => t.GoalId).OnDelete(DeleteBehavior.SetNull);
        builder.Navigation(g => g.Sessions).HasField("_sessions");
        builder.Ignore(g => g.ActiveSessions);
        builder.Ignore(g => g.UnscheduledSession);

        builder.HasIndex(g => new { g.WeekStart, g.RoleId, g.SortOrder });
    }
}
