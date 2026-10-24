using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>Пропуски: «не надо» и «не сделала» с причиной (docs/specs/skips.md, A, K, S2).</summary>
public partial class PlannerState
{
    public const string NotNeeded = "NotNeeded";
    public const string Missed = "Missed";

    private static readonly TimeSpan SkipUndoFor = TimeSpan.FromSeconds(6);

    /// <summary>Быстрые причины «не сделала» (A2).</summary>
    public static readonly IReadOnlyList<string> MissedReasons = ["заболела", "не успела", "форс-мажор", "забила"];

    public static string SkipLabel(string? kind) => kind == NotNeeded ? "не надо" : "не сделала";

    /// <summary>Дело закрыто: выполнено или пропущено — в дне оно внизу (V2), в нагрузке дня не считается.</summary>
    public static bool IsClosed(TaskDto task) => task.State is TaskStateDto.Done or TaskStateDto.Skipped;

    /// <summary>Можно ли пропустить (K3): дело в плане (день или «на неделе»), не выполнено, не проект.</summary>
    public static bool CanSkip(TaskDto task) =>
        task.Id > 0 && task.State is TaskStateDto.Planned or TaskStateDto.Skipped
        && (task.PlannedDate is not null || task.PlannedWeek is not null)
        && !(task.IsProject && task.ParentId is null);

    /// <summary>Пропустить. Шаг (не занятие) «не сделала» в дне — уходит из дня обратно в список проекта (S2).</summary>
    public async Task SkipTaskAsync(int taskId, string kind, string? reason)
    {
        if (FindTask(taskId) is not { } task || !CanSkip(task))
        {
            return;
        }

        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var day = task.PlannedDate;
        var returns = kind == Missed && task is { ParentId: not null, GoalId: null, PlannedDate: not null, State: TaskStateDto.Planned };
        _tasks[taskId] = returns
            ? task with { PlannedDate = null, StartTime = null }
            : task with { State = TaskStateDto.Skipped, SkipKind = kind, SkipReason = reason };
        SyncSession(_tasks[taskId]);

        TaskSkipResultDto? result = null;
        if (!await SaveAsync(async () => result = await api.SkipTaskAsync(taskId, kind, reason)) || result is null)
        {
            return;
        }

        _tasks[taskId] = result.Task;
        SyncSession(result.Task);
        AfterParentAutoDone(result.Parent);
        NotifyChanged();

        var message = result.ReturnedToProject
            ? $"«{task.Title}» — не сделала, вернулся в «{task.ParentTitle ?? ParentTitleOf(task)}»"
            : $"«{task.Title}» — {SkipLabel(kind)}";
        notifier.WithAction(message, "Вернуть", () => UnskipTaskAsync(taskId, result.ReturnedToProject ? day : null), SkipUndoFor);
    }

    /// <summary>Отменить пропуск (та же ✕, «Вернуть»); returnTo — вернуть шаг, ушедший в список проекта, обратно в этот день.</summary>
    public async Task UnskipTaskAsync(int taskId, DateOnly? returnTo = null)
    {
        if (FindTask(taskId) is not { } task)
        {
            return;
        }

        _tasks[taskId] = task with
        {
            State = TaskStateDto.Planned,
            SkipKind = null,
            SkipReason = null,
            PlannedDate = returnTo ?? task.PlannedDate,
        };
        SyncSession(_tasks[taskId]);

        TaskDto? result = null;
        if (await SaveAsync(async () => result = await api.UnskipTaskAsync(taskId, returnTo)) && result is not null)
        {
            _tasks[taskId] = result;
            SyncSession(result);
            NotifyChanged();
        }
    }

    public Task<List<TaskSkipDto>> GetSkipsAsync(int taskId) => api.GetTaskSkipsAsync(taskId);
}
