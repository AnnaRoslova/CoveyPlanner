namespace Planner.Client.Services;

/// <summary>
/// Какое окно боковой панели сейчас открыто: цель, роль, новая роль или дело ленты. Окна рисует страница (PlannerPage),
/// а не панель: у боковых панелей есть backdrop-filter, и position: fixed внутри неё считался бы
/// от панели, а не от окна браузера — затемнение и окно не легли бы на весь экран.
/// </summary>
public sealed class WindowHost
{
    public int? GoalId { get; private set; }

    public int? RoleId { get; private set; }

    public bool IsNewRole { get; private set; }

    /// <summary>Дело ленты «Когда-нибудь» в окне редактирования.</summary>
    public int? SomedayTaskId { get; private set; }

    public event Action? Changed;

    public void OpenGoal(int goalId) => Open(goalId, null, false);

    public void OpenRole(int roleId) => Open(null, roleId, false);

    public void OpenNewRole() => Open(null, null, true);

    public void OpenSomedayTask(int taskId) => Open(null, null, false, taskId);

    public void Close() => Open(null, null, false);

    private void Open(int? goalId, int? roleId, bool newRole, int? somedayTaskId = null)
    {
        GoalId = goalId;
        RoleId = roleId;
        IsNewRole = newRole;
        SomedayTaskId = somedayTaskId;
        Changed?.Invoke();
    }
}
