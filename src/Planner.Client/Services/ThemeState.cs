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

    // Остальные темы из docs/themes.html — после проверки этих; «Пять колец» — три варианта (docs/specs/batch-3.md, G1)
    public static readonly IReadOnlyList<ThemeInfo> Themes =
    [
        new("lotus", "Лотос"),
        new("peony", "Пион"),
        new("platinum", "Платина"),
        new("obsidian", "Обсидиан"),
        new("earth", "Земля"),
        new("water", "Вода"),
        new("void", "Пустота"),
    ];

    /// <summary>Темы «Пять колец»: у них свой декор — гравюра, облака, сакура (Components/Common/GorinDecor.razor).</summary>
    public static readonly IReadOnlySet<string> Gorin = new HashSet<string> { "earth", "water", "void" };

    public bool IsGorin => Gorin.Contains(Current);

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
