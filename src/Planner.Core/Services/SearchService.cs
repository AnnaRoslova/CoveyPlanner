using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>Быстрый поиск (BK2): группы «Дела · Мысли · Проекты · Цели», в каждой не больше <see cref="SearchService.PerGroup"/>.</summary>
public sealed record SearchResult(
    IReadOnlyList<TaskItem> Tasks, IReadOnlyList<TaskItem> Thoughts, IReadOnlyList<TaskItem> Projects, IReadOnlyList<WeeklyGoal> Goals);

/// <summary>
/// Быстрый поиск по всему (docs/specs/batch-ux.md, BK2): по названию и заметкам, без учёта регистра, «е» = «ё» (как поиск ленты);
/// корзина не ищется. Дела и цели — ближайшие к сегодня сверху, мысли — свежие сверху, проекты — открытые сверху.
/// </summary>
public class SearchService(ITaskRepository tasks, IGoalRepository goals, TimeProvider clock)
{
    public const int PerGroup = 8;

    public async Task<SearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        var needle = Normalize(query.Trim());
        if (needle.Length == 0)
        {
            return new SearchResult([], [], [], []);
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var found = (await tasks.GetNotTrashedAsync(ct))
            .Where(t => Matches(t.Title, needle) || Matches(t.Notes, needle))
            .ToList();

        var thoughts = found
            .Where(t => t is { State: TaskState.Someday, IsProject: false, ParentId: null })
            .OrderByDescending(t => t.CreatedAt)
            .Take(PerGroup).ToList();
        var projects = found
            .Where(t => t is { IsProject: true, ParentId: null })
            .OrderBy(t => t.State == TaskState.Done).ThenByDescending(t => t.CreatedAt)
            .Take(PerGroup).ToList();
        var plan = found
            .Where(t => t.IsInPlan && !t.IsProject)
            .OrderBy(t => DayOf(t) is { } day ? Math.Abs(day.DayNumber - today.DayNumber) : int.MaxValue)
            .ThenByDescending(t => t.Id)
            .Take(PerGroup).ToList();

        var thisWeek = Week.StartOf(today);
        var goalHits = (await goals.GetAllAsync(ct))
            .Where(g => Matches(g.Title, needle) || Matches(g.Notes, needle))
            .OrderBy(g => Math.Abs(g.WeekStart.DayNumber - thisWeek.DayNumber)).ThenByDescending(g => g.WeekStart)
            .Take(PerGroup).ToList();

        return new SearchResult(plan, thoughts, projects, goalHits);
    }

    /// <summary>День дела для сортировки: его день, иначе понедельник его недели «без дня».</summary>
    private static DateOnly? DayOf(TaskItem task) => task.PlannedDate ?? task.PlannedWeek;

    private static bool Matches(string? text, string needle) =>
        text is not null && Normalize(text).Contains(needle, StringComparison.Ordinal);

    // Как TextSearch клиента: регистр не важен, «ё» = «е»
    private static string Normalize(string value) => value.ToLowerInvariant().Replace('ё', 'е');
}
