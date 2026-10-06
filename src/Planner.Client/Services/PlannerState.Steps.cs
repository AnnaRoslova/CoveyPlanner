using Planner.Shared;

namespace Planner.Client.Services;

// Шаги дела (дело-чеклист): их видно только в окне дела, поэтому в состоянии экрана они не хранятся —
// окно держит свой список и меняет его через эти методы (сразу на сервере, ошибка — сообщение)
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
    public Task<StepDto?> AddStepAsync(int taskId, string title) => TryAsync(() => api.AddStepAsync(taskId, title.Trim()));

    public Task<StepDto?> RenameStepAsync(int stepId, string title) => TryAsync(() => api.RenameStepAsync(stepId, title.Trim()));

    public Task<StepDto?> SetStepDoneAsync(int stepId, bool isDone) => TryAsync(() => api.SetStepDoneAsync(stepId, isDone));

    /// <summary>Удалить шаг сразу (без корзины). false — не удалился.</summary>
    public async Task<bool> DeleteStepAsync(int stepId) =>
        await TryAsync(async () =>
        {
            await api.DeleteStepAsync(stepId);
            return true;
        });

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
