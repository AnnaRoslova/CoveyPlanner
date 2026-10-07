using Planner.Shared;

namespace Planner.Client.Services;

// Шаги дела в его окне: окно держит свой список и меняет его через эти методы (сразу на сервере, ошибка — сообщение);
// шаги на экране (в днях и раскрытых списках проектов — см. PlannerState.Projects.cs) обновляются следом
public partial class PlannerState
{
    public async Task<List<StepDto>> GetStepsAsync(int taskId)
    {
        try
        {
            return taskId > 0 ? await api.GetStepsAsync(taskId) : [];
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return [];
        }
    }

    /// <summary>Новый шаг в конец. null — не сохранился.</summary>
    public async Task<StepDto?> AddStepAsync(int taskId, string title)
    {
        var step = await TryAsync(() => api.AddStepAsync(taskId, title.Trim()));
        await RefreshStepAsync(step?.Id);
        return step;
    }

    public async Task<StepDto?> RenameStepAsync(int stepId, string title)
    {
        var step = await TryAsync(() => api.RenameStepAsync(stepId, title.Trim()));
        if (step is not null && FindTask(stepId) is { } shown)
        {
            _tasks[stepId] = shown with { Title = step.Title };
            NotifyChanged();
        }

        return step;
    }

    /// <summary>Отметить шаг из окна — как отметку дела: последний шаг отмечает проект (S12).</summary>
    public async Task<StepDto?> SetStepDoneAsync(int stepId, bool isDone)
    {
        var result = await TryAsync(() => api.SetTaskDoneAsync(stepId, isDone));
        if (result is null)
        {
            return null;
        }

        _tasks[stepId] = result.Task;
        SyncSession(result.Task);
        AfterParentAutoDone(result.Parent);
        NotifyChanged();
        return new StepDto(result.Task.Id, result.Task.ParentId ?? 0, result.Task.Title, result.Task.State == TaskStateDto.Done,
            null, result.Task.StepOrder ?? 0, result.Task.PlannedDate, result.Task.GoalId);
    }

    /// <summary>Удалить шаг сразу (без корзины). false — не удалился.</summary>
    public async Task<bool> DeleteStepAsync(int stepId)
    {
        var ok = await TryAsync(async () =>
        {
            await api.DeleteStepAsync(stepId);
            return true;
        });
        if (ok && _tasks.Remove(stepId))
        {
            RemoveSession(stepId);
            NotifyChanged();
        }

        return ok;
    }

    /// <summary>Перечитать шаг для экрана (новый — появится в раскрытом списке проекта).</summary>
    private async Task RefreshStepAsync(int? stepId)
    {
        if (stepId is not { } id)
        {
            return;
        }

        try
        {
            _tasks[id] = await api.GetTaskAsync(id);
            NotifyChanged();
        }
        catch (ApiException)
        {
            // Не страшно: шаг появится при следующей загрузке
        }
    }

    private async Task<T?> TryAsync<T>(Func<Task<T>> call)
    {
        try
        {
            await CommitPendingDeleteAsync();
            return await call();
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return default;
        }
    }
}
