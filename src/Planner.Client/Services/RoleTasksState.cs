using System.Text.Json;
using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// У каких ролей в левой панели раскрыт список «ещё дела» (дела недели с этой ролью, кроме занятий целей).
/// Запоминается в браузере (localStorage) — это настройка вида, а не данные.
/// </summary>
public sealed class RoleTasksState(IJSRuntime js)
{
    private const string Key = "planner.role-tasks-open";

    private readonly HashSet<int> _open = [];

    public event Action? Changed;

    public bool IsOpen(int roleId) => _open.Contains(roleId);

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
            // localStorage недоступен или испорчен — все списки свёрнуты
        }

        Changed?.Invoke();
    }

    public async Task ToggleAsync(int roleId)
    {
        if (!_open.Remove(roleId))
        {
            _open.Add(roleId);
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
