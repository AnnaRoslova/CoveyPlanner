using System.Text.Json;
using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Какие роли в левой панели свёрнуты (docs/specs/ui-declutter.md, U1): клик по названию роли сворачивает её цели, «Затачивание пилы» —
/// все 4 измерения сразу; кнопка в шапке панели — все роли и пилу. По умолчанию всё развёрнуто.
/// Запоминается в браузере (localStorage) — это настройка вида, а не данные. Заменяет прежний режим «цели одной строкой» (U1-a).
/// </summary>
public sealed class RoleFoldState(IJSRuntime js)
{
    private const string Key = "planner.roles-folded";

    private readonly HashSet<int> _folded = [];

    public event Action? Changed;

    /// <summary>Свёрнута секция «Затачивание пилы» целиком.</summary>
    public bool IsSawFolded { get; private set; }

    public bool IsFolded(int roleId) => _folded.Contains(roleId);

    /// <summary>Свёрнуто всё из <paramref name="roleIds"/> (и пила, если она есть на панели) — кнопка шапки тогда разворачивает.</summary>
    public bool AllFolded(IEnumerable<int> roleIds) => IsSawFolded && roleIds.All(_folded.Contains);

    public async Task InitAsync()
    {
        try
        {
            if (await js.InvokeAsync<string?>("localStorage.getItem", Key) is { } saved
                && JsonSerializer.Deserialize<Saved>(saved) is { } state)
            {
                _folded.UnionWith(state.Roles ?? []);
                IsSawFolded = state.Saw;
            }
        }
        catch (Exception e) when (e is JSException or JsonException)
        {
            // localStorage недоступен или испорчен — всё развёрнуто
        }

        Changed?.Invoke();
    }

    public Task ToggleAsync(int roleId)
    {
        if (!_folded.Remove(roleId))
        {
            _folded.Add(roleId);
        }

        return SaveAsync();
    }

    public Task ToggleSawAsync()
    {
        IsSawFolded = !IsSawFolded;
        return SaveAsync();
    }

    /// <summary>Развернуть роль (например, «Показать» на плашке ведёт к делу под ней).</summary>
    public Task UnfoldAsync(int roleId) => _folded.Remove(roleId) ? SaveAsync() : Task.CompletedTask;

    /// <summary>Кнопка шапки панели: свернуть или развернуть все роли и пилу.</summary>
    public Task SetAllAsync(IEnumerable<int> roleIds, bool folded)
    {
        _folded.Clear();
        if (folded)
        {
            _folded.UnionWith(roleIds);
        }

        IsSawFolded = folded;
        return SaveAsync();
    }

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(new Saved(_folded.Order().ToArray(), IsSawFolded)));
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }

    private sealed record Saved(int[]? Roles, bool Saw);
}
