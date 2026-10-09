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

    /// <summary>Постоянная цель, про которую спрашиваем: удалить только на этой неделе или с этой недели и дальше.</summary>
    public int? DeleteGoalId { get; private set; }

    /// <summary>Экран «Копилки» (кнопка 🫙 в шапке недели или пилюля у цели).</summary>
    public bool HabitsOpen { get; private set; }

    /// <summary>Экран «Выполненное» (строка «✓ выполненное» внизу ленты).</summary>
    public bool DoneOpen { get; private set; }

    /// <summary>Копилка, к которой экран «Копилки» прокручивается и которую подсвечивает (клик по пилюле у цели).</summary>
    public int? FocusHabitId { get; private set; }

    public event Action? Changed;

    public void OpenGoal(int goalId) => Open(goalId: goalId);

    public void OpenRole(int roleId) => Open(roleId: roleId);

    public void OpenNewRole() => Open(newRole: true);

    public void OpenSomedayTask(int taskId) => Open(somedayTaskId: taskId);

    public void OpenTask(int taskId) => Open(taskId: taskId);

    public void AskDeleteGoal(int goalId) => Open(deleteGoalId: goalId);

    public void OpenHabits(int? focusHabitId = null) => Open(habits: true, focusHabitId: focusHabitId);

    public void OpenDone() => Open(done: true);

    /// <summary>Удалить цель: постоянную — сначала спросить (только эта неделя или с этой недели и дальше), разовую — сразу.</summary>
    public Task DeleteGoalAsync(PlannerState state, int goalId)
    {
        if (state.FindGoal(goalId) is { IsRecurring: true })
        {
            AskDeleteGoal(goalId);
            return Task.CompletedTask;
        }

        return state.DeleteGoalAsync(goalId);
    }

    public void Close() => Open();

    private void Open(int? goalId = null, int? roleId = null, bool newRole = false, int? somedayTaskId = null, int? taskId = null,
        int? deleteGoalId = null, bool habits = false, int? focusHabitId = null, bool done = false)
    {
        HabitsOpen = habits;
        DoneOpen = done;
        FocusHabitId = focusHabitId;
        GoalId = goalId;
        RoleId = roleId;
        IsNewRole = newRole;
        SomedayTaskId = somedayTaskId;
        TaskId = taskId;
        DeleteGoalId = deleteGoalId;
        Changed?.Invoke();
    }
}
