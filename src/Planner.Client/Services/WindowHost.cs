namespace Planner.Client.Services;

/// <summary>
/// Какое окно левой панели сейчас открыто: цель, роль или новая роль. Окна рисует страница (PlannerPage),
/// а не карточка роли: у левой панели есть backdrop-filter, и position: fixed внутри неё считался бы
/// от панели, а не от окна браузера — затемнение и окно не легли бы на весь экран.
/// </summary>
public sealed class WindowHost
{
    public int? GoalId { get; private set; }

    public int? RoleId { get; private set; }

    public bool IsNewRole { get; private set; }

    public event Action? Changed;

    public void OpenGoal(int goalId) => Open(goalId, null, false);

    public void OpenRole(int roleId) => Open(null, roleId, false);

    public void OpenNewRole() => Open(null, null, true);

    public void Close() => Open(null, null, false);

    private void Open(int? goalId, int? roleId, bool newRole)
    {
        GoalId = goalId;
        RoleId = roleId;
        IsNewRole = newRole;
        Changed?.Invoke();
    }
}
