using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>Тема оформления: ключ (атрибут data-theme, css/themes.css) и короткое имя для переключателя.</summary>
public sealed record ThemeInfo(string Key, string Name);

/// <summary>
/// Выбранная тема оформления. Сама тема — это CSS (css/themes.css) по атрибуту data-theme на &lt;html&gt;;
/// выбор хранится в браузере (js/theme.js), потом — в профиле пользователя, когда появятся аккаунты.
/// </summary>
public sealed class ThemeState(IJSRuntime js)
{
    public const string DefaultKey = "lotus";

    // Пока четыре темы; остальные из docs/themes.html — после проверки этих
    public static readonly IReadOnlyList<ThemeInfo> Themes =
    [
        new("lotus", "Лотос"),
        new("peony", "Пион"),
        new("platinum", "Платина"),
        new("obsidian", "Обсидиан"),
    ];

    private IJSObjectReference? _module;

    public string Current { get; private set; } = DefaultKey;

    public event Action? Changed;

    /// <summary>Узнать тему, которую index.html уже поставил из сохранённого выбора.</summary>
    public async Task InitAsync()
    {
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
            var key = await _module.InvokeAsync<string>("getTheme");
            Current = Themes.Any(t => t.Key == key) ? key : DefaultKey;
            if (Current != key)
            {
                // Сохранена тема, которой (пока) нет — возвращаем тему по умолчанию
                await _module.InvokeVoidAsync("setTheme", Current);
            }

            Changed?.Invoke();
        }
        catch (JSException)
        {
            // Без JS остаётся тема по умолчанию
        }
    }

    public async Task SetAsync(string key)
    {
        if (key == Current || Themes.All(t => t.Key != key))
        {
            return;
        }

        Current = key;
        Changed?.Invoke();
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setTheme", key);
        }
    }
}
