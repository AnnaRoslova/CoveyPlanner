namespace Planner.Core.Entities;

/// <summary>Роль в шаблоне серии целей (таблица GoalSeriesRoles). Живёт только внутри серии.</summary>
public class SeriesRole
{
    // Для EF Core
    private SeriesRole()
    {
    }

    public SeriesRole(int roleId) => RoleId = roleId;

    public int RoleId { get; private set; }
}
