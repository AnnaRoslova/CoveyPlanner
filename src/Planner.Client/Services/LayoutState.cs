using Microsoft.JSInterop;

namespace Planner.Client.Services;

public enum PanelSide
{
    Left,
    Right,
}

/// <summary>Как сейчас показана боковая панель.</summary>
public enum PanelMode
{
    /// <summary>Панель на своём месте.</summary>
    Open,

    /// <summary>Тонкая полоска-шторка (свёрнута вручную или не хватает места).</summary>
    Strip,

    /// <summary>Не хватает места, но панель попросили показать — поверх недели.</summary>
    Overlay,
}

/// <summary>
/// Раскладка экрана: какие боковые панели открыты. Пользователь сворачивает их кнопкой-шторкой
/// (запоминается в localStorage); если места под неделю не хватает, панели сворачиваются сами —
/// сначала левая (роли), потом правая (лента).
/// </summary>
public sealed class LayoutState(IJSRuntime js) : IAsyncDisposable
{
    // Размеры синхронны с app.css (--left-width, --right-width, --strip-width, --day-min-width)
    // и отступами .app / .days
    private const double LeftWidth = 240;
    private const double RightWidth = 250;
    private const double StripWidth = 36;
    private const double AppChrome = 14 * 2 + 14 * 2;      // поля страницы + зазоры между панелями
    private const double MinWeekWidth = 7 * 126 + 6 * 5 + 10 * 2; // 7 дней + зазоры + поля недели

    private const string LeftKey = "planner.left-panel";
    private const string RightKey = "planner.right-panel";

    private DotNetObjectReference<LayoutState>? _selfRef;
    private IJSObjectReference? _module;

    private bool _leftWanted = true;
    private bool _rightWanted = true;
    private PanelSide? _peek;
    private double _width = double.MaxValue; // пока неизвестна — считаем, что места много

    public event Action? Changed;

    public PanelMode Left => ModeOf(PanelSide.Left);

    public PanelMode Right => ModeOf(PanelSide.Right);

    /// <summary>Прочитать сохранённые настройки и начать следить за шириной окна.</summary>
    public async Task InitAsync()
    {
        _leftWanted = await ReadAsync(LeftKey) != "closed";
        _rightWanted = await ReadAsync(RightKey) != "closed";

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/layout.js");
            _selfRef = DotNetObjectReference.Create(this);
            _width = await _module.InvokeAsync<double>("watchWidth", _selfRef);
        }
        catch (JSException)
        {
            // Без JS просто не сворачиваем панели автоматически
        }

        Changed?.Invoke();
    }

    [JSInvokable]
    public void OnResize(double width)
    {
        _width = width;
        if (_peek is { } side && ModeOf(side) != PanelMode.Overlay)
        {
            _peek = null; // места стало достаточно — панель просто открыта
        }

        Changed?.Invoke();
    }

    /// <summary>Кнопка-шторка: открытую панель свернуть, свёрнутую — показать (на месте или поверх недели).</summary>
    public async Task ToggleAsync(PanelSide side)
    {
        if (_peek == side)
        {
            _peek = null; // только заглянули — сохранённый выбор не меняем
        }
        else if (ModeOf(side) == PanelMode.Open)
        {
            await SetWantedAsync(side, false);
        }
        else
        {
            await SetWantedAsync(side, true);
            if (ModeOf(side) == PanelMode.Strip)
            {
                _peek = side; // на месте не помещается — показываем поверх
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Показать панель, если она свёрнута (например, «🛠 Проекты» из шапки недели или с плашки).</summary>
    public async Task ShowAsync(PanelSide side)
    {
        if (ModeOf(side) != PanelMode.Open && _peek != side)
        {
            await ToggleAsync(side);
        }
    }

    /// <summary>Закрыть панель, показанную поверх недели (клик мимо неё).</summary>
    public void ClosePeek()
    {
        _peek = null;
        Changed?.Invoke();
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

    private PanelMode ModeOf(PanelSide side)
    {
        // Правая важнее: она открыта, если помещается при свёрнутой левой
        var rightOpen = _rightWanted && _width >= AppChrome + StripWidth + RightWidth + MinWeekWidth;
        var leftOpen = _leftWanted
                       && _width >= AppChrome + LeftWidth + (rightOpen ? RightWidth : StripWidth) + MinWeekWidth;

        var open = side == PanelSide.Left ? leftOpen : rightOpen;
        return open ? PanelMode.Open : _peek == side ? PanelMode.Overlay : PanelMode.Strip;
    }

    private async Task SetWantedAsync(PanelSide side, bool wanted)
    {
        if (side == PanelSide.Left)
        {
            _leftWanted = wanted;
        }
        else
        {
            _rightWanted = wanted;
        }

        await WriteAsync(side == PanelSide.Left ? LeftKey : RightKey, wanted ? "open" : "closed");
    }

    private async Task<string?> ReadAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (JSException)
        {
            return null; // localStorage недоступен (приватный режим) — просто не запоминаем
        }
    }

    private async Task WriteAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch (JSException)
        {
        }
    }
}
