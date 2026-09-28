using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>
/// Ставит дело в нужное место (лента / приоритеты дня / сетка) и пересчитывает порядок соседей.
/// Общий помощник для TaskService и GoalService. Сам не сохраняет изменения.
/// </summary>
public class TaskPlacer(ITaskRepository tasks, ICategoryRepository categories, ISettingsRepository settings)
{
    public async Task PlaceAsync(TaskItem task, TaskTarget target, CancellationToken ct = default)
    {
        switch (target)
        {
            case SomedayTarget someday:
                if (await categories.FindAsync(someday.CategoryId, ct) is null)
                {
                    throw new NotFoundException("Категория", someday.CategoryId);
                }

                var categorySiblings = await tasks.GetSomedayInCategoryAsync(someday.CategoryId, ct);
                task.MoveToSomeday(someday.CategoryId);
                SortOrdering.Place(categorySiblings, task, someday.Index);
                break;

            case DayPrioritiesTarget day:
                var daySiblings = await tasks.GetDayPrioritiesAsync(day.Date, ct);
                task.PlanForDay(day.Date);
                SortOrdering.Place(daySiblings, task, day.Index);
                break;

            case GridTarget grid:
                var gridSettings = await settings.GetAsync(ct);
                var start = gridSettings.SnapStart(grid.Start);
                var duration = gridSettings.NormalizeDuration(start, grid.DurationMinutes);
                task.ScheduleAt(grid.Date, start, duration);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Неизвестное место назначения.");
        }
    }

    /// <summary>Вернуть дело в ленту наверх его категории (или во «Входящие», если категории нет).</summary>
    public async Task ReturnToSomedayAsync(TaskItem task, CancellationToken ct = default)
    {
        var category = task.CategoryId is { } id ? await categories.FindAsync(id, ct) : null;
        category ??= await categories.GetInboxAsync(ct);
        await PlaceAsync(task, new SomedayTarget(category.Id, 0), ct);
    }
}
