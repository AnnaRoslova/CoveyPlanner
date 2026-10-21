using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>Вкладка экрана «📈 Итоги» (docs/specs/batch-ux.md, BI2).</summary>
public enum TotalsTab { Habits, Done, Report }

/// <summary>
/// Какая вкладка «📈 Итогов» открывается кнопкой 📈 (и клавишей H): последняя открытая, по умолчанию «Привычки».
/// Запоминается в браузере (localStorage) — это настройка вида, а не данные.
/// </summary>
public sealed class TotalsState(IJSRuntime js)
{
    private const string Key = "planner.totals-tab";
    private const string ViewKey = "planner.report-view";

    public TotalsTab Last { get; private set; } = TotalsTab.Habits;

    /// <summary>Вкладка «Отчёт» показывает дашборд, а не список (batch-ux.md, BD1).</summary>
    public bool Dashboard { get; private set; }

    public async Task InitAsync()
    {
        try
        {
            if (Enum.TryParse<TotalsTab>(await js.InvokeAsync<string?>("localStorage.getItem", Key), out var tab))
            {
                Last = tab;
            }

            Dashboard = await js.InvokeAsync<string?>("localStorage.getItem", ViewKey) == "dashboard";
        }
        catch (JSException)
        {
            // localStorage недоступен — «Привычки»
        }
    }

    public async Task RememberAsync(TotalsTab tab)
    {
        Last = tab;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, tab.ToString());
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }

    public async Task SetDashboardAsync(bool dashboard)
    {
        Dashboard = dashboard;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", ViewKey, dashboard ? "dashboard" : "list");
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }
}
