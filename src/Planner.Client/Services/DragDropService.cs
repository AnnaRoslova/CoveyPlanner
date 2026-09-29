using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Что сообщает wwwroot/js/dnd.js, когда запись бросили в зону.
/// Kind — что тащили (data-drag), Zone и Key — куда (data-drop, data-key),
/// Index — позиция среди записей зоны без самой перетаскиваемой.
/// </summary>
public sealed record DropEvent(string Kind, int Id, string Zone, string? Key, int Index);

/// <summary>
/// Мост между JS-движком перетаскивания и состоянием экрана:
/// JS следит за указателем и рисует подсказки, а что означает «бросили сюда» — решается здесь.
/// </summary>
public sealed class DragDropService(IJSRuntime js, PlannerState state) : IAsyncDisposable
{
    // Виды перетаскиваемого (data-drag) и зоны (data-drop) — те же строки, что в разметке
    public const string SomedayItem = "someday";
    public const string SomedayZone = "someday";

    private DotNetObjectReference<DragDropService>? _selfRef;
    private IJSObjectReference? _module;

    public async Task InitAsync()
    {
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/dnd.js");
            _selfRef = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("init", _selfRef);
        }
        catch (JSException)
        {
            // Без JS перетаскивания просто не будет — остальное работает
        }
    }

    [JSInvokable]
    public async Task OnDrop(DropEvent drop)
    {
        switch (drop)
        {
            // Лента → лента: порядок в категории или перенос в другую категорию
            case { Kind: SomedayItem, Zone: SomedayZone } when int.TryParse(drop.Key, out var categoryId):
                await state.MoveSomedayTaskAsync(drop.Id, categoryId, drop.Index);
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
