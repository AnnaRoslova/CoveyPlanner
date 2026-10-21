using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Напоминание о недельном обзоре (docs/specs/batch-ux.md, BR1): в воскресенье после 12:00 при первом открытии — плашка
/// «Неделя заканчивается · Недельный обзор», раз в неделю; пропустили — в понедельник до 12:00 ещё раз. Неделю напоминания
/// помнит браузер (localStorage) — это не данные.
/// </summary>
public sealed class ReviewReminder(IJSRuntime js, Notifier notifier, WindowHost windows)
{
    private const string Key = "planner.review-reminded";

    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(12);

    /// <summary>Пора напомнить: воскресенье с 12:00 или понедельник до 12:00.</summary>
    public static bool IsTime(DateTime now) =>
        (now.DayOfWeek == DayOfWeek.Sunday && now.Hour >= 12) || (now.DayOfWeek == DayOfWeek.Monday && now.Hour < 12);

    public async Task CheckAsync()
    {
        var now = DateTime.Now;
        if (!IsTime(now))
        {
            return;
        }

        // В воскресенье и в понедельник утром неделя обзора одна и та же — напоминание одно
        var week = WeeklyReviewWeek(now).ToString("yyyy-MM-dd");
        try
        {
            if (await js.InvokeAsync<string?>("localStorage.getItem", Key) == week)
            {
                return;
            }

            await js.InvokeVoidAsync("localStorage.setItem", Key, week);
        }
        catch (JSException)
        {
            return; // без памяти браузера напоминание повторялось бы каждый раз — лучше не напоминать
        }

        notifier.WithAction("Неделя заканчивается", "Недельный обзор", () =>
        {
            windows.OpenReview();
            return Task.CompletedTask;
        }, ShowFor);
    }

    private static DateOnly WeeklyReviewWeek(DateTime now) => Components.Week.WeeklyReview.ReviewWeek(now);
}
