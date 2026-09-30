using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Следит, чтобы «сегодня» на экране было настоящим: проверяет дату в полночь и когда окно
/// снова становится активным (вкладку открыли, окно получило фокус, компьютер проснулся).
/// Если дата сменилась — сообщает <see cref="PlannerState"/>, а тот при необходимости листает неделю.
/// </summary>
public sealed class TodayWatcher(PlannerState state, IJSRuntime js) : IAsyncDisposable
{
    private DateOnly _today = RuDates.Today;
    private Timer? _midnightTimer;
    private DotNetObjectReference<TodayWatcher>? _selfRef;

    public async Task InitAsync()
    {
        ScheduleMidnightCheck();

        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/layout.js");
            _selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("watchActivation", _selfRef);
        }
        catch (JSException)
        {
            // Без JS остаётся проверка в полночь
        }
    }

    /// <summary>Окно снова активно (вызывается из layout.js).</summary>
    [JSInvokable]
    public Task OnActivated() => CheckAsync();

    private async Task CheckAsync()
    {
        // Таймер мог сработать позже (сон компьютера) — всегда заводим заново от текущего времени
        ScheduleMidnightCheck();

        var today = RuDates.Today;
        if (today == _today)
        {
            return;
        }

        var previous = _today;
        _today = today;
        await state.OnTodayChangedAsync(previous, today);
    }

    /// <summary>Проверка через секунду после ближайшей полуночи.</summary>
    private void ScheduleMidnightCheck()
    {
        var now = DateTime.Now;
        var delay = now.Date.AddDays(1) - now + TimeSpan.FromSeconds(1);
        _midnightTimer?.Dispose();
        _midnightTimer = new Timer(_ => _ = CheckAsync(), null, delay, Timeout.InfiniteTimeSpan);
    }

    public ValueTask DisposeAsync()
    {
        _midnightTimer?.Dispose();
        _selfRef?.Dispose();
        return ValueTask.CompletedTask;
    }
}
