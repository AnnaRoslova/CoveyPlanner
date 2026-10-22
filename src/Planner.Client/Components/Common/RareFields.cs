using Microsoft.JSInterop;

namespace Planner.Client.Components.Common;

/// <summary>
/// Редкие поля окна (docs/specs/ui-declutter.md, U2.2): какие раскрыты (клик по сводке или «+ …»). Раскрытое остаётся раскрытым до закрытия
/// окна, не запоминается. После раскрытия — фокус в поле (js/focus.js), вызывается из OnAfterRenderAsync окна.
/// </summary>
public sealed class RareFields(IJSRuntime js)
{
    private readonly HashSet<string> _open = [];
    private string? _focus;

    public bool IsOpen(string key) => _open.Contains(key);

    /// <summary>Раскрыть поле; после перерисовки фокус встанет в него.</summary>
    public void Open(string key)
    {
        _open.Add(key);
        _focus = key;
    }

    /// <summary>Свернуть поле обратно в «+ …» («×» у раскрытого поля, batch-3.md, M1.2).</summary>
    public void Close(string key) => _open.Remove(key);

    /// <summary>Раскрыть без фокуса (поле открыли не человек, а другое действие).</summary>
    public void Show(string key) => _open.Add(key);

    public async Task AfterRenderAsync()
    {
        if (_focus is not { } key)
        {
            return;
        }

        _focus = null;
        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/focus.js");
            await module.InvokeVoidAsync("focusField", key);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or TaskCanceledException)
        {
            // Окно уже закрыли — фокусировать нечего
        }
    }

    /// <summary>Первая строка текста для сводки «📝 …» (CSS обрежет длинную многоточием).</summary>
    public static string FirstLine(string? text) =>
        (text ?? "").Trim().Split('\n', 2)[0].Trim();
}
