using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>
/// Дела для клиента вместе с тем, что видно на строке: название проекта у шага («↳ Ремонт») и шаги видимых проектов
/// (раскрытие «▸», «☑ 2/5»). Родители и шаги подгружаются одним запросом на список.
/// </summary>
public class TaskViews(ITaskRepository tasks)
{
    public async Task<TaskDto> ToDtoAsync(TaskItem task, CancellationToken ct = default) =>
        (await ToDtosAsync([task], ct))[0];

    public async Task<List<TaskDto>> ToDtosAsync(IReadOnlyCollection<TaskItem> items, CancellationToken ct = default)
    {
        var parentIds = items.Where(t => t.ParentId is not null).Select(t => t.ParentId!.Value).Distinct().ToList();
        var titles = parentIds.Count == 0
            ? []
            : (await tasks.FindManyAsync(parentIds, ct)).ToDictionary(p => p.Id, p => p.Title);
        return items.Select(t => t.ToDto(t.ParentId is { } id ? titles.GetValueOrDefault(id) : null)).ToList();
    }

    /// <summary>Шаги (без корзины) дел-проектов из <paramref name="parents"/> — у шагов родителя не бывает шагов.</summary>
    public async Task<List<TaskDto>> StepsOfAsync(IEnumerable<TaskItem> parents, CancellationToken ct = default)
    {
        var byId = parents.Where(p => !p.IsStep).DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        if (byId.Count == 0)
        {
            return [];
        }

        return (await tasks.GetStepsOfAsync(byId.Keys, ct))
            .Where(s => s.State != TaskState.Trashed)
            .Select(s => s.ToDto(byId[s.ParentId!.Value].Title))
            .ToList();
    }
}
