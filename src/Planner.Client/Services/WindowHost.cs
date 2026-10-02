namespace Planner.Client.Services;

/// <summary>
/// Какое окно сейчас открыто: цель, роль, новая роль, дело ленты или дело плана (его открывают и из дня,
/// и из списка «ещё дела» в левой панели). Окна рисует страница (PlannerPage), а не панель: у боковых панелей
/// есть backdrop-filter, и position: fixed внутри неё считался бы от панели, а не от окна браузера —
/// затемнение и окно не легли бы на весь экран.
/// </summary>
public sealed class WindowHost
{
    public int? GoalId { get; private set; }

    public int? RoleId { get; private set; }

    public bool IsNewRole { get; private set; }

    /// <summary>Дело ленты «Когда-нибудь» в окне редактирования.</summary>
    public int? SomedayTaskId { get; private set; }

    /// <summary>Дело плана (в дне) в окне редактирования.</summary>
    public int? TaskId { get; private set; }

    public event Action? Changed;

    public void OpenGoal(int goalId) => Open(goalId: goalId);

    public void OpenRole(int roleId) => Open(roleId: roleId);

    public void OpenNewRole() => Open(newRole: true);

    public void OpenSomedayTask(int taskId) => Open(somedayTaskId: taskId);

    public void OpenTask(int taskId) => Open(taskId: taskId);

    public void Close() => Open();

    private void Open(int? goalId = null, int? roleId = null, bool newRole = false, int? somedayTaskId = null, int? taskId = null)
    {
        GoalId = goalId;
        RoleId = roleId;
        IsNewRole = newRole;
        SomedayTaskId = somedayTaskId;
        TaskId = taskId;
        Changed?.Invoke();
    }
}
