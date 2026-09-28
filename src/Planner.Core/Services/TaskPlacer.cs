using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>
/// Ставит дело в нужное место (лента / день) и пересчитывает порядок соседей.
/// Общий помощник для сервисов дел, целей и ролей. Сам не сохраняет изменения.
/// </summary>
public class TaskPlacer(ITaskRepository tasks, ICategoryRepository categories)
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

            case DayTarget day:
                task.PlanForDay(day.Date);

                // Ручной порядок только у дел без времени; дело со временем встанет по времени
                if (task.StartTime is null)
                {
                    var daySiblings = await tasks.GetUntimedOnDayAsync(day.Date, ct);
                    SortOrdering.Place(daySiblings, task, day.Index);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Неизвестное место назначения.");
        }
    }

    /// <summary>Поставить дело без времени в конец своего дня (после снятия времени или возврата из корзины).</summary>
    public async Task MoveToEndOfDayAsync(TaskItem task, CancellationToken ct = default)
    {
        if (task.PlannedDate is { } date && task.StartTime is null)
        {
            var siblings = await tasks.GetUntimedOnDayAsync(date, ct);
            SortOrdering.Place(siblings, task, int.MaxValue);
        }
    }

    /// <summary>
    /// Вернуть дело в ленту наверх его категории. Если своей категории нет —
    /// в <paramref name="fallbackCategoryId"/>; если и её не передали — <see cref="CategoryRequiredException"/>.
    /// </summary>
    public async Task ReturnToSomedayAsync(TaskItem task, int? fallbackCategoryId, CancellationToken ct = default)
    {
        var category = task.CategoryId is { } own ? await categories.FindAsync(own, ct) : null;
        if (category is null && fallbackCategoryId is { } fallback)
        {
            category = await categories.FindAsync(fallback, ct) ?? throw new NotFoundException("Категория", fallback);
        }

        if (category is null)
        {
            throw new CategoryRequiredException();
        }

        await PlaceAsync(task, new SomedayTarget(category.Id, 0), ct);
    }
}
