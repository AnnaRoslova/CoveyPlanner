using Planner.Shared;

namespace Planner.Client.Services;

// Удаление с «Вернуть»: на экране — сразу, на сервере — через несколько секунд
public partial class PlannerState
{
    /// <summary>Сколько висит плашка «Удалено «…» · Вернуть» — и сколько сервер ждёт удаления.</summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(6);

    private PendingDelete? _pendingDelete;

    /// <summary>Есть удаление, которое ещё можно вернуть (на сервер оно пока не ушло).</summary>
    public bool HasPendingDelete => _pendingDelete is not null;

    /// <summary>Сколько раз что-то уходило в корзину — по нему значок корзины коротко «вздрагивает».</summary>
    public int TrashPulse { get; private set; }

    /// <summary>
    /// Удалить с возможностью вернуть. Экран меняется сразу (<paramref name="applyLocally"/>), а запрос на сервер
    /// (<paramref name="commit"/>) уходит через <see cref="UndoWindow"/> — или раньше, перед любым другим запросом:
    /// после других изменений «Вернуть» уже не предлагается. «Вернуть» просто возвращает экран, как было, —
    /// на сервере ничего не происходило. Закрыли приложение раньше — удаление не случилось (безопасная сторона).
    /// </summary>
    /// <param name="what">Что удалено — для плашки: «Удалено «…»».</param>
    /// <param name="reloadAfter">После удаления перечитать всё с сервера: последствия сложные (занятия целей).</param>
    private async Task DeleteWithUndoAsync(string what, Action applyLocally, Func<Task> commit, bool reloadAfter = false)
    {
        await CommitPendingDeleteAsync(); // прежнее удаление — уже насовсем

        var before = TakeSnapshot();
        applyLocally();
        if (TrashCount > before.TrashCount)
        {
            TrashPulse++;
        }
        var pending = new PendingDelete(before, commit, reloadAfter);
        _pendingDelete = pending;
        pending.ToastVersion = notifier.WithAction(what, "Вернуть", UndoDeleteAsync, UndoWindow, nearTrash: true);
        NotifyChanged();
        _ = CommitLaterAsync(pending);
    }

    private async Task CommitLaterAsync(PendingDelete pending)
    {
        await Task.Delay(UndoWindow);
        if (_pendingDelete == pending)
        {
            await CommitPendingDeleteAsync();
        }
    }

    /// <summary>«Вернуть»: экран — как до удаления; на сервер удаление так и не уходит.</summary>
    public Task UndoDeleteAsync()
    {
        if (_pendingDelete is { } pending)
        {
            _pendingDelete = null;
            notifier.Dismiss(pending.ToastVersion);
            RestoreSnapshot(pending.Before);
            NotifyChanged();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Отправить отложенное удаление на сервер сейчас (окно ушло в фон, другое изменение, срок вышел).
    /// <paramref name="reload"/> = false — перечитывать после удаления будет вызывающий (см. <see cref="SaveAsync"/>).
    /// Возвращает, нужно ли перечитать состояние.
    /// </summary>
    public async Task<bool> CommitPendingDeleteAsync(bool reload = true)
    {
        if (_pendingDelete is not { } pending)
        {
            return false;
        }

        _pendingDelete = null;
        notifier.Dismiss(pending.ToastVersion);
        try
        {
            await pending.Commit();
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            await ReloadAsync(); // не удалилось — показываем, как на самом деле
            return false;
        }

        if (pending.ReloadAfter && reload)
        {
            await ReloadAsync();
        }

        return pending.ReloadAfter;
    }

    private Snapshot TakeSnapshot() =>
        new(new Dictionary<int, TaskDto>(_tasks), [.. Roles], [.. Categories], [.. Goals], TrashCount, [.. Habits], [.. Vacations]);

    private void RestoreSnapshot(Snapshot snapshot)
    {
        _tasks.Clear();
        foreach (var (id, task) in snapshot.Tasks)
        {
            _tasks[id] = task;
        }

        Roles = [.. snapshot.Roles];
        Categories = [.. snapshot.Categories];
        Goals = [.. snapshot.Goals];
        TrashCount = snapshot.TrashCount;
        Habits = [.. snapshot.Habits];
        Vacations = [.. snapshot.Vacations];
    }

    /// <summary>
    /// Как удаление цели выглядит на экране сразу (так же решает сервер): выполненные занятия — обычные дела,
    /// невыполненные — в корзину, занятие без дня — наверх своей категории ленты (её нет — «неразобранного»).
    /// </summary>
    private void RemoveGoalLocally(WeeklyGoalDto goal)
    {
        Goals.RemoveAll(g => g.Id == goal.Id);
        foreach (var task in SessionTasks(goal))
        {
            if (task.State == TaskStateDto.Done)
            {
                _tasks[task.Id] = task with { GoalId = null }; // выполненное остаётся обычным делом
            }
            else if (task.PlannedDate is null)
            {
                PlaceInFeedLocally(task with { GoalId = null }, IsFeedPlace(task.CategoryId) ? task.CategoryId : null, 0);
            }
            else
            {
                _tasks.Remove(task.Id); // невыполненное — в корзину
                TrashCount++;
            }
        }
    }

    /// <summary>Для плашки: длинное название сокращаем. Цель, роль, категория, привычка — в корзину не попадают: «Удалено «…»».</summary>
    private static string Deleted(string title) => $"Удалено «{Short(title)}»";

    /// <summary>Дело — в корзине, а не удалено (batch-ux.md, BG-b): «„Купить хлеб“ в корзине».</summary>
    private static string Trashed(string title) => $"«{Short(title)}» в корзине";

    private static string Short(string title) => title.Length > 40 ? title[..40].TrimEnd() + "…" : title;

    private sealed record Snapshot(
        Dictionary<int, TaskDto> Tasks, List<RoleDto> Roles, List<CategoryDto> Categories, List<WeeklyGoalDto> Goals, int TrashCount,
        List<HabitDto> Habits, List<VacationDto> Vacations);

    private sealed record PendingDelete(Snapshot Before, Func<Task> Commit, bool ReloadAfter)
    {
        public int ToastVersion { get; set; }
    }
}
