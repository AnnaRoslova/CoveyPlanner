using Microsoft.AspNetCore.Components;
using Planner.Client.Services;

namespace Planner.Client.Components;

/// <summary>Базовый компонент: перерисовывается при любом изменении <see cref="PlannerState"/>.</summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject]
    protected PlannerState State { get; set; } = null!;

    protected override void OnInitialized() => State.Changed += OnStateChanged;

    public virtual void Dispose()
    {
        State.Changed -= OnStateChanged;
        GC.SuppressFinalize(this);
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);
}
