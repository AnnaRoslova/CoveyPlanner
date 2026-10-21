using Planner.Shared;

namespace Planner.Client.Services;

// Давно лежащие мысли (docs/specs/batch-ux.md, BS): срок — с сервера (⚙ Настройки), метка возраста в ленте,
// плашка о перешагнувших срок при загрузке и в полночь, «Оставить».
public partial class PlannerState
{
    /// <summary>Срок «давно лежат», месяцев (BS2).</summary>
    public int StaleMonths { get; private set; } = 3;

    /// <summary>Показать ленту в режиме «давно лежат» (плашка «Посмотреть») — переключает правая панель.</summary>
    public event Action? StaleRequested;

    public void RequestStale() => StaleRequested?.Invoke();

    /// <summary>Мысль ленты (не шаг, не проект).</summary>
    public static bool IsThought(TaskDto task) => task is { State: TaskStateDto.Someday, ParentId: null, IsProject: false };

    /// <summary>BS5: мысль, у которой срок прошёл от последнего изменения или «Оставить».</summary>
    public bool IsStale(TaskDto task) =>
        IsThought(task) && task.UpdatedAt is { } updated && ThoughtAge.IsStale(updated, task.KeptAt, StaleMonths, DateTime.UtcNow);

    /// <summary>Старые мысли ленты — самые давние сверху.</summary>
    public IReadOnlyList<TaskDto> StaleThoughts() =>
        _tasks.Values.Where(t => t.Id > 0 && IsStale(t)).OrderBy(t => t.KeptAt > t.UpdatedAt ? t.KeptAt : t.UpdatedAt).ThenBy(t => t.Id).ToList();

    public async Task SetStaleMonthsAsync(int months)
    {
        try
        {
            StaleMonths = (await api.SetSettingsAsync(months)).StaleMonths;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    /// <summary>«Оставить» (BS4): мысль снова свежая на один срок.</summary>
    public async Task KeepAsync(int taskId)
    {
        try
        {
            var kept = await api.KeepTaskAsync(taskId);
            _tasks[kept.Id] = kept;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }

    /// <summary>
    /// BS4: плашка о мыслях, перешагнувших срок с прошлого напоминания (сервер помечает их показанными):
    /// одна — «„…“ лежит уже 3 мес · Посмотреть · Оставить», несколько — «5 мыслей лежат больше 3 мес · Посмотреть».
    /// </summary>
    public async Task CheckStaleAsync()
    {
        StaleNoticeDto notice;
        try
        {
            notice = await api.TakeStaleNoticesAsync();
        }
        catch (ApiException)
        {
            return; // напоминание не главное — без него можно
        }

        var duration = TimeSpan.FromSeconds(12);
        if (notice.Thoughts is [var one])
        {
            notifier.WithActions($"«{one.Title}» лежит уже {notice.Months} мес", "Посмотреть", () =>
            {
                RequestStale();
                return Task.CompletedTask;
            }, "Оставить", () => KeepAsync(one.Id), duration);
        }
        else if (notice.Thoughts.Count > 1)
        {
            var n = notice.Thoughts.Count;
            notifier.WithAction($"{n} {ThoughtsWord(n)} {(n % 10 == 1 && n % 100 != 11 ? "лежит" : "лежат")} больше {notice.Months} мес", "Посмотреть", () =>
            {
                RequestStale();
                return Task.CompletedTask;
            }, duration);
        }
    }

    private static string ThoughtsWord(int n) =>
        (n % 100 is >= 11 and <= 14) ? "мыслей"
        : (n % 10) switch { 1 => "мысль", >= 2 and <= 4 => "мысли", _ => "мыслей" };
}
