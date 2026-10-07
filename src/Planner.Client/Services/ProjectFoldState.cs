using System.Text.Json;
using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// У каких проектов (дел с шагами) раскрыт список шагов — в дне и в ленте («▸ Ремонт ☑ 2/5»).
/// Запоминается в браузере (localStorage) — это настройка вида, а не данные.
/// </summary>
public sealed class ProjectFoldState(IJSRuntime js)
{
    private const string Key = "planner.projects-open";

    private readonly HashSet<int> _open = [];

    public event Action? Changed;

    public bool IsOpen(int taskId) => _open.Contains(taskId);

    public async Task InitAsync()
    {
        try
        {
            var saved = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            if (saved is not null)
            {
                _open.UnionWith(JsonSerializer.Deserialize<int[]>(saved) ?? []);
            }
        }
        catch (Exception e) when (e is JSException or JsonException)
        {
            // localStorage недоступен или испорчен — все проекты свёрнуты
        }

        Changed?.Invoke();
    }

    public async Task ToggleAsync(int taskId)
    {
        if (!_open.Remove(taskId))
        {
            _open.Add(taskId);
        }

        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(_open.Order()));
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }
}
