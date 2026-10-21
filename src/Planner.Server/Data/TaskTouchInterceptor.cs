using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Planner.Core.Entities;

namespace Planner.Server.Data;

/// <summary>
/// «Последнее изменение» дела (docs/specs/batch-ux.md, BS1) — в одном месте, а не в каждом методе: при сохранении дело,
/// у которого изменилось что-то по существу (название, заметки, категория, проект/шаг, состояние), получает UpdatedAt = сейчас.
/// Порядок (SortOrder, StepOrder), «Оставить» и отметка о плашке возраст не освежают.
/// </summary>
public sealed class TaskTouchInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly string[] Meaningful =
    [
        nameof(TaskItem.Title), nameof(TaskItem.Notes), nameof(TaskItem.CategoryId), nameof(TaskItem.ParentId),
        nameof(TaskItem.IsProject), nameof(TaskItem.State),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Touch(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Touch(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Touch(DbContext? db)
    {
        if (db is null)
        {
            return;
        }

        db.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in db.ChangeTracker.Entries<TaskItem>().Where(e => e.State == EntityState.Modified && ChangedMeaningfully(e)))
        {
            entry.Entity.Touch(now);
        }
    }

    private static bool ChangedMeaningfully(EntityEntry<TaskItem> entry) =>
        Meaningful.Any(name => entry.Property(name) is { IsModified: true } p && !Equals(p.OriginalValue, p.CurrentValue));
}
