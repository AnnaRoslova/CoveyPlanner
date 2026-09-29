namespace Planner.Client.Services;

/// <summary>Короткие всплывающие сообщения внизу экрана (ошибки сохранения, подсказки).</summary>
public class Notifier
{
    public event Action? Changed;

    public string? Message { get; private set; }

    public bool IsError { get; private set; }

    /// <summary>Растёт с каждым сообщением — чтобы таймер скрытия не погасил более новое.</summary>
    public int Version { get; private set; }

    public void Error(string message) => Show(message, isError: true);

    public void Info(string message) => Show(message, isError: false);

    public void Dismiss(int version)
    {
        if (version == Version)
        {
            Message = null;
            Changed?.Invoke();
        }
    }

    private void Show(string message, bool isError)
    {
        Message = message;
        IsError = isError;
        Version++;
        Changed?.Invoke();
    }
}
