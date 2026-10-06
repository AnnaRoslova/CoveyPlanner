using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>Запись «Выполненного»: дело, день отметки, сводка проекта и «N недель» (null — у дела нет выполненных шагов).</summary>
public sealed record DoneEntry(TaskItem Task, DateOnly Date, ProjectSummary? Project, int? Weeks, IReadOnlyList<TaskItem> Steps);

/// <summary>
/// Группа «Выполненного» — категория ленты (null — «без категории»: «неразобранное» и дела удалённых категорий),
/// счёт по годам (свежие сверху) и записи (свежие сверху).
/// </summary>
public sealed record DoneGroup(Category? Category, IReadOnlyList<(int Year, int Count)> Years, IReadOnlyList<DoneEntry> Entries);

/// <summary>
/// «Выполненное» (V1–V6) — только вид: выполненные дела, которые хоть раз лежали в ленте, по их текущей категории.
/// Дела, созданные прямо в неделе, шаги (они — внутри родителя) сюда не попадают. Ничего не хранится.
/// </summary>
public class DoneArchiveService(ITaskRepository tasks, ICategoryRepository categories, TimeProvider clock)
{
    public async Task<IReadOnlyList<DoneGroup>> GetAsync(CancellationToken ct = default)
    {
        var done = await tasks.GetDoneFromSomedayAsync(ct);
        var steps = (await tasks.GetStepsOfAsync(done.Select(t => t.Id).ToList(), ct)).ToLookup(s => s.ParentId!.Value);
        var entries = done.Select(task => Entry(task, steps[task.Id].ToList())).ToList();

        // Группы — в порядке категорий ленты; «без категории» — в конце
        var ordered = (await categories.GetAllAsync(ct)).Select(c => (Category?)c).Append(null);
        return ordered
            .Select(category => (category, list: entries.Where(e => e.Task.CategoryId == category?.Id).ToList()))
            .Where(g => g.list.Count > 0)
            .Select(g => new DoneGroup(
                g.category,
                g.list.GroupBy(e => e.Date.Year).OrderByDescending(y => y.Key).Select(y => (y.Key, y.Count())).ToList(),
                g.list))
            .ToList();
    }

    /// <summary>V5, V6: дата — день отметки; «N недель» — от недели первого выполненного шага (или занятия) до недели отметки.</summary>
    private DoneEntry Entry(TaskItem task, List<TaskItem> steps)
    {
        var date = LocalDate(task.CompletedAt!.Value);
        var firstStep = steps
            .Where(s => s is { State: TaskState.Done, CompletedAt: not null })
            .Select(s => LocalDate(s.CompletedAt!.Value))
            .DefaultIfEmpty()
            .Min();
        int? weeks = firstStep == default
            ? null
            : Math.Max(1, (Week.StartOf(date).DayNumber - Week.StartOf(firstStep).DayNumber) / Week.DaysInWeek + 1);
        return new DoneEntry(task, date, ProjectRules.Summarize(steps), weeks, steps);
    }

    /// <summary>Отметка хранится в UTC — день считается по часам пользователя (сервер на его компьютере).</summary>
    private DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone));
}
