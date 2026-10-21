namespace Planner.Client.Services;

/// <summary>
/// Нет связи с сервером (docs/specs/batch-ux.md, BN). Изменения (не GET) уходят на сервер строго по одному и по порядку;
/// не прошедшее из-за связи изменение остаётся на экране и ждёт повтора — вызывающий код просто ждёт ответа дольше,
/// следующие изменения встают за ним (BN1). Повтор — через 2, 5, 10, 30 с и дальше каждые 30 с, сразу — по «Повторить сейчас»,
/// событию online и возврату в окно (BN3). Ответ сервера с ошибкой — как раньше, исключение у вызывающего (BN4).
/// Чтения ждут, пока очередь опустеет, — перечитывание не затирает несохранённое (BN6).
/// </summary>
public sealed class ConnectionQueue
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource _retry = NewSignal();
    private TaskCompletionSource _drained = Completed();
    private int _waiting;
    private int _savedWhileOffline;

    /// <summary>Связи нет: последнее изменение не дошло, очередь ждёт повтора.</summary>
    public bool IsOffline { get; private set; }

    /// <summary>Сколько изменений ещё не сохранено (в очереди и в попытке).</summary>
    public int Pending => _waiting;

    /// <summary>Связь пропала / вернулась / изменилось число несохранённых.</summary>
    public event Action? Changed;

    /// <summary>Связь вернулась и очередь ушла; число — сколько изменений сохранено после обрыва (BN3).</summary>
    public event Action<int>? Restored;

    /// <summary>Отправить изменение: по порядку, при обрыве связи — ждать и повторять, пока не дойдёт.</summary>
    public async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        if (Interlocked.Increment(ref _waiting) == 1)
        {
            _drained = NewSignal();
        }

        Changed?.Invoke();
        await _gate.WaitAsync();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var response = await send();
                    if (IsOffline)
                    {
                        _savedWhileOffline++;
                    }

                    return response;
                }
                catch (HttpRequestException)
                {
                    if (!IsOffline)
                    {
                        IsOffline = true;
                        _savedWhileOffline = 0;
                        Changed?.Invoke();
                    }

                    await WaitRetryAsync(RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)]);
                }
            }
        }
        finally
        {
            _gate.Release();
            if (Interlocked.Decrement(ref _waiting) == 0)
            {
                _drained.TrySetResult();
                if (IsOffline)
                {
                    IsOffline = false;
                    Restored?.Invoke(_savedWhileOffline);
                }
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Чтение — после того как очередь изменений опустеет.</summary>
    public Task WhenDrainedAsync() => _drained.Task;

    /// <summary>«Повторить сейчас», событие online, окно снова активно.</summary>
    public void RetryNow() => _retry.TrySetResult();

    private async Task WaitRetryAsync(TimeSpan delay)
    {
        var signal = _retry = NewSignal();
        await Task.WhenAny(Task.Delay(delay), signal.Task);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource Completed()
    {
        var done = NewSignal();
        done.SetResult();
        return done;
    }
}
