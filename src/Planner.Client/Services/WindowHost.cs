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

    /// <summary>Экран «📊 Отчёт» (кнопка в шапке недели, N5).</summary>
    public bool ReportOpen { get; private set; }

    /// <summary>Экран «Как это устроено» (batch-ux.md, BG3): словарь, схема, клавиши.</summary>
    public bool HelpOpen { get; private set; }

    /// <summary>Окно «⚙ Настройки» (batch-ux.md, BI5).</summary>
    public bool SettingsOpen { get; private set; }

    /// <summary>«Моя миссия» (batch-ux.md, BM).</summary>
    public bool MissionOpen { get; private set; }

    /// <summary>Недельный обзор (W, «Итоги»; batch-ux.md, BR).</summary>
    public bool ReviewOpen { get; private set; }

    /// <summary>«Пользователи и приглашения» — только у администратора (accounts.md, U1).</summary>
    public bool AdminOpen { get; private set; }

    /// <summary>Окошко пропуска дела (skips.md, A2).</summary>
    public int? SkipTaskId { get; private set; }

    /// <summary>«Аккаунт»: устройства, выгрузка, удаление (accounts.md, P1–P3).</summary>
    public bool AccountOpen { get; private set; }

    /// <summary>Быстрый поиск (Ctrl+K, /; batch-ux.md, BK2).</summary>
    public bool SearchOpen { get; private set; }

    /// <summary>Мини-календарь «перейти к дате» под подписью недели (клик по подписи, G; batch-3.md, J1). Рисует его неделя (WeekView).</summary>
    public bool CalendarOpen { get; private set; }

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

    public void OpenReport() => Open(report: true);

    public void OpenHelp() => Open(help: true);

    public void OpenSettings() => Open(settings: true);

    public void OpenSearch() => Open(search: true);

    public void OpenReview() => Open(review: true);

    public void OpenMission() => Open(mission: true);

    public void OpenCalendar() => Open(calendar: true);

    public void OpenAdmin() => Open(admin: true);

    public void OpenAccount() => Open(account: true);

    public void OpenSkip(int taskId) => Open(skipTaskId: taskId);

    /// <summary>«📈 Итоги» (BI2) на вкладке: «Привычки» — экран привычек, «Выполненное», «Отчёт».</summary>
    public void OpenTotals(TotalsTab tab)
    {
        switch (tab)
        {
            case TotalsTab.Done: OpenDone(); break;
            case TotalsTab.Report: OpenReport(); break;
            default: OpenHabits(); break;
        }
    }

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
        int? deleteGoalId = null, bool habits = false, int? focusHabitId = null, bool done = false, bool report = false, bool help = false, bool settings = false, bool search = false, bool review = false, bool mission = false, bool calendar = false, bool admin = false, bool account = false, int? skipTaskId = null)
    {
        SkipTaskId = skipTaskId;
        AdminOpen = admin;
        AccountOpen = account;
        HabitsOpen = habits;
        DoneOpen = done;
        ReportOpen = report;
        HelpOpen = help;
        SettingsOpen = settings;
        SearchOpen = search;
        ReviewOpen = review;
        MissionOpen = mission;
        CalendarOpen = calendar;
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
