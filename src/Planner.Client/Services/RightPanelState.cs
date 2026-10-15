using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Что показывает правая панель: ленту «Когда-нибудь» или «🛠 Проекты» (docs/specs/next-batch.md, N6.8) — проекты на месте ленты,
/// чтобы их шаги тащить прямо в дни. Режим запоминается в браузере; поиск и фильтр по категории — до перезагрузки.
/// </summary>
public sealed class RightPanelState : IDisposable
{
    private const string Key = "planner.right-mode";
    private const string ProjectsMode = "projects";

    private readonly IJSRuntime _js;
    private readonly LayoutState _layout;
    private readonly PlannerState _state;

    public RightPanelState(IJSRuntime js, LayoutState layout, PlannerState state)
    {
        _js = js;
        _layout = layout;
        _state = state;
        _state.ProjectsRequested += OnProjectsRequested;
    }

    public event Action? Changed;

    /// <summary>Показаны проекты (иначе — лента).</summary>
    public bool IsProjects { get; private set; }

    /// <summary>Поиск в панели проектов.</summary>
    public string ProjectQuery { get; set; } = "";

    /// <summary>Фильтр проектов: null — все категории, иначе ключ группы (Id категории или «неразобранное» — без категории).</summary>
    public string? CategoryFilter { get; set; }

    public async Task InitAsync()
    {
        try
        {
            IsProjects = await _js.InvokeAsync<string?>("localStorage.getItem", Key) == ProjectsMode;
        }
        catch (JSException)
        {
            // localStorage недоступен — лента
        }

        Changed?.Invoke();
    }

    /// <summary>Кнопка 🛠 в шапке недели: проекты ↔ лента.</summary>
    public Task ToggleAsync() => IsProjects ? ShowFeedAsync() : ShowProjectsAsync();

    /// <summary>Показать проекты; query — сразу с этим поиском (строка «🛠 в проектах: N» из поиска ленты).</summary>
    public async Task ShowProjectsAsync(string? query = null)
    {
        if (query is not null)
        {
            ProjectQuery = query;
            CategoryFilter = null;
        }

        await SetModeAsync(true);
        await _layout.ShowAsync(PanelSide.Right);
    }

    public Task ShowFeedAsync() => SetModeAsync(false);

    /// <summary>Плашка «🛠 Проекты»: панель на этом проекте — без фильтров, проект коротко подсвечен.</summary>
    private async void OnProjectsRequested(int? projectId)
    {
        ProjectQuery = "";
        CategoryFilter = null;
        await ShowProjectsAsync();
        if (projectId is { } id)
        {
            _state.Flash(id);
        }
    }

    private async Task SetModeAsync(bool projects)
    {
        IsProjects = projects;
        Changed?.Invoke();
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", Key, projects ? ProjectsMode : "feed");
        }
        catch (JSException)
        {
            // Не запомнили — не страшно
        }
    }

    public void Dispose() => _state.ProjectsRequested -= OnProjectsRequested;
}
