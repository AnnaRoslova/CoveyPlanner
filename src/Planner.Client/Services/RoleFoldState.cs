using System.Text.Json;
using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Какие роли в левой панели свёрнуты (docs/specs/ui-declutter.md, U1; batch-3.md, M4): клик по названию роли сворачивает её цели,
/// клик по разделу («Затачивание пилы», «Роли») — цели всех его ролей (названия ролей остаются видны); кнопка в шапке панели —
/// все роли обоих разделов. Раздел целиком в одну строку не сворачивается. По умолчанию всё развёрнуто.
/// Запоминается в браузере (localStorage) — это настройка вида, а не данные.
/// </summary>
public sealed class RoleFoldState(IJSRuntime js)
{
    private const string Key = "planner.roles-folded";

    private readonly HashSet<int> _folded = [];

    public event Action? Changed;

    public bool IsFolded(int roleId) => _folded.Contains(roleId);

    /// <summary>Свёрнуты все роли из <paramref name="roleIds"/> — клик по разделу (или кнопка шапки) тогда разворачивает.</summary>
    public bool AllFolded(IEnumerable<int> roleIds)
    {
        var ids = roleIds.ToList();
        return ids.Count > 0 && ids.All(_folded.Contains);
    }

    public async Task InitAsync()
    {
        try
        {
            // Прежний флаг «пила свёрнута одной строкой» (Saw) больше не читается: раздел целиком не сворачивается (M4.4)
            if (await js.InvokeAsync<string?>("localStorage.getItem", Key) is { } saved
                && JsonSerializer.Deserialize<Saved>(saved) is { } state)
            {
                _folded.UnionWith(state.Roles ?? []);
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

    /// <summary>Развернуть роль (например, «Показать» на плашке ведёт к делу под ней).</summary>
    public Task UnfoldAsync(int roleId) => _folded.Remove(roleId) ? SaveAsync() : Task.CompletedTask;

    /// <summary>Клик по разделу (M4.1): хоть одна роль раскрыта — свернуть все; все свёрнуты — раскрыть все.</summary>
    public Task ToggleSectionAsync(IEnumerable<int> roleIds)
    {
        var ids = roleIds.ToList();
        return SetAllAsync(ids, !AllFolded(ids));
    }

    /// <summary>Свернуть или развернуть цели всех ролей из списка (раздел — M4.1, кнопка шапки — M4.3).</summary>
    public Task SetAllAsync(IEnumerable<int> roleIds, bool folded)
    {
        foreach (var id in roleIds)
        {
            if (folded)
            {
                _folded.Add(id);
            }
            else
            {
                _folded.Remove(id);
            }
        }

        return SaveAsync();
    }

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(new Saved(_folded.Order().ToArray())));
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }

    private sealed record Saved(int[]? Roles);
}
