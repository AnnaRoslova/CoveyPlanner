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

    /// <summary>Подсказка «📎 +1» — со скрепкой, которая падает в банку привычки.</summary>
    public bool IsClip { get; private set; }

    /// <summary>Надпись на кнопке-действии; null — кнопки нет.</summary>
    public string? ActionLabel { get; private set; }

    private Func<Task>? _action;

    /// <summary>Вторая кнопка («выполнено · в корзину · вернуть»): надпись; null — её нет.</summary>
    public string? SecondActionLabel { get; private set; }

    private Func<Task>? _secondAction;

    /// <summary>Плашка — про ленту и корзину («Удалено «…» · Вернуть», «выполнено»): показывается у значка корзины внизу ленты.</summary>
    public bool IsNearTrash { get; private set; }

    /// <summary>Сколько сообщение висит на экране.</summary>
    public TimeSpan Duration { get; private set; } = DefaultDuration;

    /// <summary>Растёт с каждым сообщением — чтобы таймер скрытия не погасил более новое.</summary>
    public int Version { get; private set; }

    public void Error(string message) => Show(message, isError: true);

    public void Info(string message) => Show(message, isError: false);

    /// <summary>Скрепка легла в банку привычки: «+1 в «Тренировки» · 103 · неделя 3 из 3 ✓» — на пару секунд.</summary>
    public void Clip(string message) =>
        Show(message, isError: false, duration: ClipDuration, isClip: true);

    public static readonly TimeSpan ClipDuration = TimeSpan.FromSeconds(2.5);

    /// <summary>Сообщение с кнопкой. Возвращает номер сообщения — чтобы потом убрать именно его.</summary>
    public int WithAction(string message, string actionLabel, Func<Task> action, TimeSpan duration, bool nearTrash = false) =>
        Show(message, isError: false, actionLabel, action, duration, nearTrash: nearTrash);

    /// <summary>Сообщение с двумя кнопками («в корзину» · «вернуть»).</summary>
    public int WithActions(
        string message, string actionLabel, Func<Task> action, string secondLabel, Func<Task> secondAction, TimeSpan duration,
        bool nearTrash = false)
    {
        var version = Show(message, isError: false, actionLabel, action, duration, nearTrash: nearTrash, silent: true);
        SecondActionLabel = secondLabel;
        _secondAction = secondAction;
        Changed?.Invoke();
        return version;
    }

    /// <summary>Нажали кнопку: сообщение закрывается, действие выполняется (один раз).</summary>
    public async Task RunActionAsync(int version, bool second = false)
    {
        if (version != Version || (second ? _secondAction : _action) is not { } action)
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
            SecondActionLabel = null;
            _secondAction = null;
            Changed?.Invoke();
        }
    }

    private int Show(string message, bool isError, string? actionLabel = null, Func<Task>? action = null, TimeSpan? duration = null,
        bool isClip = false, bool nearTrash = false, bool silent = false)
    {
        IsNearTrash = nearTrash;
        SecondActionLabel = null;
        _secondAction = null;
        Message = message;
        IsError = isError;
        IsClip = isClip;
        ActionLabel = actionLabel;
        _action = action;
        Duration = duration ?? DefaultDuration;
        Version++;
        if (!silent)
        {
            Changed?.Invoke();
        }

        return Version;
    }
}
