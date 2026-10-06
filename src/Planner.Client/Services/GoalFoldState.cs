using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Цели в левой панели развёрнуты (название целиком, дни занятий, банка привычки, «2/5») или свёрнуты в одну строку
/// «название · процент». Все цели сразу — одной кнопкой в шапке панели; по умолчанию развёрнуты.
/// Запоминается в браузере (localStorage) — это настройка вида, а не данные.
/// </summary>
public sealed class GoalFoldState(IJSRuntime js)
{
    private const string Key = "planner.goals-folded";

    public bool IsFolded { get; private set; }

    public async Task InitAsync()
    {
        try
        {
            IsFolded = await js.InvokeAsync<string?>("localStorage.getItem", Key) == "1";
        }
        catch (JSException)
        {
            // localStorage недоступен — цели развёрнуты
        }
    }

    public async Task ToggleAsync()
    {
        IsFolded = !IsFolded;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, IsFolded ? "1" : "0");
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }
}
