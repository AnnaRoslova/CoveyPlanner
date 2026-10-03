using Planner.Core.Sorting;

namespace Planner.Core.Entities;

/// <summary>
/// Роль цели (таблица GoalRoles): у цели может быть несколько ролей, и в каждой она стоит на своём месте
/// (<see cref="SortOrder"/> — порядок среди целей этой роли на неделе). Живёт только внутри цели.
/// </summary>
public class GoalRole : ISortable
{
    // Для EF Core
    private GoalRole()
    {
    }

    public GoalRole(int roleId) => RoleId = roleId;

    public int RoleId { get; private set; }

    public int SortOrder { get; set; }
}
