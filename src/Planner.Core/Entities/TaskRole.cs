namespace Planner.Core.Entities;

/// <summary>Роль дела: у дела может быть несколько ролей (таблица TaskRoles). Живёт только внутри дела.</summary>
public class TaskRole
{
    // Для EF Core
    private TaskRole()
    {
    }

    public TaskRole(int roleId) => RoleId = roleId;

    public int RoleId { get; private set; }
}
