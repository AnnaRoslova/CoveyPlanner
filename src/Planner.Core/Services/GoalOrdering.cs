using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>
/// Место цели в списке целей роли на неделе. У цели с несколькими ролями в каждой роли своё место
/// (<see cref="GoalRole.SortOrder"/>). Сам не сохраняет изменения.
/// </summary>
public class GoalOrdering(IGoalRepository goals)
{
    /// <summary>
    /// Поставить цель на позицию <paramref name="index"/> в списке роли <paramref name="roleId"/> (у цели эта роль уже есть).
    /// <paramref name="unsaved"/> — цели, созданные в этом же запросе: запросы к базе их ещё не видят.
    /// </summary>
    public async Task PlaceAsync(
        WeeklyGoal goal, int roleId, int index, IEnumerable<WeeklyGoal>? unsaved = null, CancellationToken ct = default)
    {
        var link = goal.RoleLink(roleId)
                   ?? throw new InvalidOperationException($"У цели {goal.Id} нет роли {roleId}.");
        var siblings = (await goals.GetForRoleAsync(roleId, goal.WeekStart, ct))
            .Concat(unsaved ?? [])
            .Where(g => g != goal && g.WeekStart == goal.WeekStart && g.HasRole(roleId))
            .Distinct()
            .Select(g => g.RoleLink(roleId)!)
            .OrderBy(l => l.SortOrder)
            .ToList();
        SortOrdering.Place(siblings, link, index);
    }

    /// <summary>Новые роли цели — в конец их списков.</summary>
    public async Task PlaceAtEndAsync(
        WeeklyGoal goal, IEnumerable<GoalRole> added, IEnumerable<WeeklyGoal>? unsaved = null, CancellationToken ct = default)
    {
        foreach (var link in added.ToList())
        {
            await PlaceAsync(goal, link.RoleId, int.MaxValue, unsaved, ct);
        }
    }
}
