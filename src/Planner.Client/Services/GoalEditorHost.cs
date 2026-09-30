namespace Planner.Client.Services;

/// <summary>
/// Какая цель сейчас открыта в окне. Окно рисует страница (PlannerPage), а не карточка роли:
/// у левой панели есть backdrop-filter, и position: fixed внутри неё считался бы от панели, а не от окна.
/// </summary>
public sealed class GoalEditorHost
{
    public int? GoalId { get; private set; }

    public event Action? Changed;

    public void Open(int goalId)
    {
        GoalId = goalId;
        Changed?.Invoke();
    }

    public void Close()
    {
        GoalId = null;
        Changed?.Invoke();
    }
}
