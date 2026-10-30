using System.Text.Json;
using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Подсказка «щёлкни, чтобы записать» в пустом месте дня (docs/specs/polish-3.md, H): при наведении — пока не добавлено три дела
/// кликом по пустому месту дня; на касании (наведения нет) — один раз, ненадолго. Запоминается в браузере (localStorage) — это вид, а не данные.
/// </summary>
public sealed class DayHintState(IJSRuntime js)
{
    private const string Key = "planner.day-hint";

    /// <summary>Сколько дел, добавленных кликом по пустому месту, хватает, чтобы подсказка больше не нужна (H2).</summary>
    public const int Enough = 3;

    private Saved _saved = new(0, false);

    public event Action? Changed;

    /// <summary>Подсказка при наведении на пустое место (H1).</summary>
    public bool ShowOnHover => _saved.Adds < Enough;

    /// <summary>Касание: подсказку в пустых днях ещё не показывали (H3) — показать один раз.</summary>
    public bool ShowOnceForTouch { get; private set; }

    public async Task InitAsync()
    {
        try
        {
            if (await js.InvokeAsync<string?>("localStorage.getItem", Key) is { } json)
            {
                _saved = JsonSerializer.Deserialize<Saved>(json) ?? _saved;
            }

            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/layout.js");
            if (!_saved.TouchShown && ShowOnHover && await module.InvokeAsync<bool>("isTouch"))
            {
                ShowOnceForTouch = true;
                await SaveAsync(_saved with { TouchShown = true }); // показали — больше не показываем, даже если вкладку сразу закрыли
            }
        }
        catch (Exception e) when (e is JSException or JsonException)
        {
            // localStorage недоступен или испорчен — подсказка при наведении, как в начале
        }

        Changed?.Invoke();
    }

    /// <summary>Дело добавлено из строки, открытой кликом по пустому месту дня (H2).</summary>
    public async Task CountAddAsync()
    {
        if (!ShowOnHover)
        {
            return;
        }

        await SaveAsync(_saved with { Adds = _saved.Adds + 1 });
        if (!ShowOnHover)
        {
            Changed?.Invoke();
        }
    }

    private async Task SaveAsync(Saved saved)
    {
        _saved = saved;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(saved));
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }

    private sealed record Saved(int Adds, bool TouchShown);
}
