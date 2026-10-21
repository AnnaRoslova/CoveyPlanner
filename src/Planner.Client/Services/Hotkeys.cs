using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Горячие клавиши (docs/specs/batch-ux.md, BK): клавиши ловит js/hotkeys.js (он же решает, когда их не перехватывать — BK1),
/// здесь — что делает каждая. Фокус в «Записать мысль…» и «+ дело» в сегодняшнем дне ставит js после перерисовки.
/// </summary>
public sealed class Hotkeys(
    IJSRuntime js, PlannerState state, WindowHost windows, RightPanelState rightPanel, LayoutState layout, TotalsState totals, Notifier notifier)
    : IAsyncDisposable
{
    private DotNetObjectReference<Hotkeys>? _selfRef;

    public async Task InitAsync()
    {
        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/hotkeys.js");
            _selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("watch", _selfRef);
        }
        catch (JSException)
        {
            // Без клавиш можно — всё есть и мышью
        }
    }

    [JSInvokable]
    public async Task Run(string action)
    {
        switch (action)
        {
            case "thought": // N: «Записать мысль…» — вкладка «Мысли», правая колонка раскрыта
                await rightPanel.ShowFeedAsync();
                await layout.ShowAsync(PanelSide.Right);
                break;
            case "day": // D: сегодняшний день текущей недели, «+ дело» открывает js
                if (state.WeekStart != RuDates.WeekStartOf(RuDates.Today))
                {
                    await state.TodayAsync();
                }

                break;
            case "prev":
                await state.PreviousWeekAsync();
                break;
            case "next":
                await state.NextWeekAsync();
                break;
            case "today":
                await state.TodayAsync();
                break;
            case "search":
                windows.OpenSearch();
                break;
            case "totals":
                windows.OpenTotals(totals.Last);
                break;
            case "help":
                windows.OpenHelp();
                break;
            case "tabs": // P: «Мысли ⇄ Проекты»
                await rightPanel.ToggleAsync();
                await layout.ShowAsync(PanelSide.Right);
                break;
            case "undo": // Ctrl+Z: «Вернуть» на плашке, пока она видна
                await notifier.UndoAsync();
                break;
        }
    }

    public ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        return ValueTask.CompletedTask;
    }
}
