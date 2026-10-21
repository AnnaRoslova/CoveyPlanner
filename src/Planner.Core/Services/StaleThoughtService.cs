using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;
using Planner.Core.Thoughts;

namespace Planner.Core.Services;

/// <summary>
/// Давно лежащие мысли (docs/specs/batch-ux.md, BS): список старых (самые старые сверху), плашка о перешагнувших срок
/// (один раз за перешагивание) и «Оставить».
/// </summary>
public class StaleThoughtService(ITaskRepository tasks, ISettingsRepository settings, IUnitOfWork unitOfWork, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Старые мысли и срок: самые старые (по последнему изменению или «Оставить») сверху.</summary>
    public async Task<(int Months, IReadOnlyList<TaskItem> Thoughts)> GetStaleAsync(CancellationToken ct = default)
    {
        var months = (await settings.GetAsync(ct)).StaleMonths;
        var now = Now;
        var stale = (await tasks.GetSomedayAsync(ct))
            .Where(t => StaleRules.IsStale(t, months, now))
            .OrderBy(t => StaleRules.StaleSince(t, months)).ThenBy(t => t.Id)
            .ToList();
        return (months, stale);
    }

    /// <summary>
    /// BS4: мысли, о перешагивании срока которых ещё не напоминали; отмечаются как показанные — второй раз не придут.
    /// </summary>
    public async Task<(int Months, IReadOnlyList<TaskItem> Thoughts)> TakeNewlyStaleAsync(CancellationToken ct = default)
    {
        var months = (await settings.GetAsync(ct)).StaleMonths;
        var now = Now;
        var fresh = (await tasks.GetSomedayAsync(ct))
            .Where(t => StaleRules.NeedsNotice(t, months, now))
            .OrderBy(t => StaleRules.StaleSince(t, months)).ThenBy(t => t.Id)
            .ToList();
        foreach (var thought in fresh)
        {
            thought.MarkStaleNotified(now);
        }

        if (fresh.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return (months, fresh);
    }

    /// <summary>«Оставить»: мысль снова свежая на один срок.</summary>
    public async Task<TaskItem> KeepAsync(int id, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(id, ct) ?? throw new NotFoundException("Дело", id);
        if (!StaleRules.IsThought(task))
        {
            throw new DomainException("Оставить можно только мысль из ленты.");
        }

        task.Keep(Now);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }
}
