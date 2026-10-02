namespace Planner.Client.Services;

/// <summary>
/// Короткие всплывающие сообщения внизу экрана (ошибки сохранения, подсказки).
/// У сообщения может быть кнопка-действие: «Вернуть» после удаления, «Показать» после переноса.
/// </summary>
public class Notifier
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    public event Action? Changed;

    public string? Message { get; private set; }

    public bool IsError { get; private set; }

    /// <summary>Надпись на кнопке-действии; null — кнопки нет.</summary>
    public string? ActionLabel { get; private set; }

    private Func<Task>? _action;

    /// <summary>Сколько сообщение висит на экране.</summary>
    public TimeSpan Duration { get; private set; } = DefaultDuration;

    /// <summary>Растёт с каждым сообщением — чтобы таймер скрытия не погасил более новое.</summary>
    public int Version { get; private set; }

    public void Error(string message) => Show(message, isError: true);

    public void Info(string message) => Show(message, isError: false);

    /// <summary>Сообщение с кнопкой. Возвращает номер сообщения — чтобы потом убрать именно его.</summary>
    public int WithAction(string message, string actionLabel, Func<Task> action, TimeSpan duration) =>
        Show(message, isError: false, actionLabel, action, duration);

    /// <summary>Нажали кнопку: сообщение закрывается, действие выполняется (один раз).</summary>
    public async Task RunActionAsync(int version)
    {
        if (version != Version || _action is not { } action)
        {
            return;
        }

        Dismiss(version);
        await action();
    }

    public void Dismiss(int version)
    {
        if (version == Version && Message is not null)
        {
            Message = null;
            ActionLabel = null;
            _action = null;
            Changed?.Invoke();
        }
    }

    private int Show(string message, bool isError, string? actionLabel = null, Func<Task>? action = null, TimeSpan? duration = null)
    {
        Message = message;
        IsError = isError;
        ActionLabel = actionLabel;
        _action = action;
        Duration = duration ?? DefaultDuration;
        Version++;
        Changed?.Invoke();
        return Version;
    }
}
