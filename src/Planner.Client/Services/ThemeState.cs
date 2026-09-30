using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>Тема оформления: ключ (атрибут data-theme, css/themes.css), название и характер для переключателя.</summary>
public sealed record ThemeInfo(string Key, string Name, string Mood);

/// <summary>
/// Выбранная тема оформления. Сама тема — это CSS (css/themes.css) по атрибуту data-theme на &lt;html&gt;;
/// выбор хранится в браузере (js/theme.js), потом — в профиле пользователя, когда появятся аккаунты.
/// </summary>
public sealed class ThemeState(IJSRuntime js)
{
    public const string DefaultKey = "lotus";

    public static readonly IReadOnlyList<ThemeInfo> Themes =
    [
        new("lotus", "Лотос", "мята и «зефир», в точку"),
        new("peony", "Пион", "кремовая бумага, эвкалипт"),
        new("dune", "Дюна", "тауп, какао, песок"),
        new("graphite", "Графит", "строгая сине-серая"),
        new("powder", "Пудра", "беж и серо-розовый"),
        new("sea", "Море", "бирюза, голубой, песок"),
        new("lavender", "Лаванда", "сирень и шалфей"),
        new("night", "Ночь", "тёмная"),
        new("neon", "Неон", "чёрный, жёлтый, неон"),
        new("android", "Андроид", "белый, голубой светодиод"),
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
