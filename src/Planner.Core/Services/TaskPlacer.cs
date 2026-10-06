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
                if (someday.CategoryId is { } categoryId && await categories.FindAsync(categoryId, ct) is null)
                {
                    throw new NotFoundException("Категория", categoryId);
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
    /// Сняли отметку с выполненного в ленте (D6): дело — на своё прежнее место в своей категории (SortOrder не менялся);
    /// категории нет (удалили) — наверх «неразобранного».
    /// </summary>
    public async Task ReturnToOwnPlaceAsync(TaskItem task, CancellationToken ct = default)
    {
        if (task.CategoryId is { } own && await categories.FindAsync(own, ct) is not null)
        {
            var siblings = await tasks.GetSomedayInCategoryAsync(own, ct);
            SortOrdering.Place(siblings, task, siblings.Count(s => s != task && s.SortOrder < task.SortOrder));
        }
        else
        {
            await PlaceAsync(task, new SomedayTarget(null, 0), ct);
        }
    }

    /// <summary>
    /// Вернуть дело в ленту наверх его категории, а если своей категории нет (создано в плане,
    /// категорию удалили) — наверх «неразобранного».
    /// </summary>
    public async Task ReturnToSomedayAsync(TaskItem task, CancellationToken ct = default)
    {
        var category = task.CategoryId is { } own ? await categories.FindAsync(own, ct) : null;
        await PlaceAsync(task, new SomedayTarget(category?.Id, 0), ct);
    }
}
