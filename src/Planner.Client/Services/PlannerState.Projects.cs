using Planner.Shared;

namespace Planner.Client.Services;

// Проекты — дела с шагами (docs/specs/tasks-steps-done.md): шаги видимых проектов лежат вместе с делами (_tasks),
// шаг в дне — та же запись; «☑ 2/5», раскрытие, перенос шага в список (снять с дня), «+ шаг»
public partial class PlannerState
{
    /// <summary>Шаги проекта по порядку, без корзины (и шаги-занятия целей).</summary>
    public IReadOnlyList<TaskDto> StepsOf(int parentId) =>
        _tasks.Values
            .Where(t => t.ParentId == parentId && t.State != TaskStateDto.Trashed)
            .OrderBy(t => t.StepOrder ?? int.MaxValue).ThenBy(t => t.Id)
            .ToList();

    /// <summary>Шаги в счёте проекта: без занятий целей (S17).</summary>
    public IReadOnlyList<TaskDto> CountedStepsOf(int parentId) => StepsOf(parentId).Where(s => s.GoalId is null).ToList();

    /// <summary>Сводка «☑ 2/5 · 12 занятий»; null — у дела нет шагов (оно не проект).</summary>
    public ProjectSummaryDto? ProjectOf(int taskId)
    {
        var steps = StepsOf(taskId);
        if (steps.Count == 0)
        {
            return null;
        }

        var counted = steps.Where(s => s.GoalId is null).ToList();
        return new ProjectSummaryDto(
            counted.Count(s => s.State == TaskStateDto.Done),
            counted.Count,
            steps.Count(s => s.GoalId is not null && s.State == TaskStateDto.Done));
    }

    /// <summary>Подпись строки проекта: «☑ 2/5», «12 занятий» или оба.</summary>
    public static string? ProjectLabel(ProjectSummaryDto? project) => project switch
    {
        null => null,
        { StepsTotal: > 0, SessionsDone: > 0 } p => $"☑ {p.StepsDone}/{p.StepsTotal} · {SessionsWord(p.SessionsDone)}",
        { StepsTotal: > 0 } p => $"☑ {p.StepsDone}/{p.StepsTotal}",
        { SessionsDone: > 0 } p => SessionsWord(p.SessionsDone),
        _ => null,
    };

    /// <summary>«1 занятие», «3 занятия», «12 занятий».</summary>
    public static string SessionsWord(int n) =>
        $"{n} " + (n % 10 == 1 && n % 100 != 11 ? "занятие"
            : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? "занятия"
            : "занятий");

    /// <summary>Название проекта шага: из загруженного родителя, иначе — как прислал сервер.</summary>
    public string? ParentTitleOf(TaskDto task) =>
        task.ParentId is { } parentId ? FindTask(parentId)?.Title ?? task.ParentTitle : null;

    /// <summary>Загрузить дело, если его нет на экране (проект шага на другой неделе) — для его окна. false — не нашлось.</summary>
    public async Task<bool> EnsureTaskAsync(int taskId)
    {
        if (FindTask(taskId) is not null)
        {
            return true;
        }

        try
        {
            _tasks[taskId] = await api.GetTaskAsync(taskId);
            return true;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Шаг бросили на строку его проекта или в раскрытый список шагов (S20): снять с дня и поставить в список на место index.
    /// Шаг другого проекта сюда не встаёт (сменить проект — полем «Проект» в окне).
    /// </summary>
    public async Task MoveStepToListAsync(int stepId, int parentId, int index)
    {
        if (stepId < 0 || FindTask(stepId) is not { } step || step.ParentId != parentId)
        {
            return;
        }

        var siblings = StepsOf(parentId).Where(s => s.Id != stepId).ToList();
        var current = StepsOf(parentId).ToList().FindIndex(s => s.Id == stepId);
        if (step.PlannedDate is null && current == Math.Clamp(index, 0, siblings.Count))
        {
            return; // туда же, где лежал
        }

        siblings.Insert(Math.Clamp(index, 0, siblings.Count), step with { PlannedDate = null, StartTime = null });
        for (var i = 0; i < siblings.Count; i++)
        {
            _tasks[siblings[i].Id] = siblings[i] with { StepOrder = i };
        }

        SyncSession(_tasks[stepId]);
        await SaveAsync(() => api.MoveTaskAsync(stepId, new StepListTargetDto(parentId, index)));
    }

    /// <summary>«+ шаг» в раскрытом списке: новый шаг в конец (роли — копия ролей проекта). false — не сохранился.</summary>
    public async Task<bool> AddProjectStepAsync(int parentId, string title)
    {
        title = title.Trim();
        if (title.Length == 0 || parentId < 0 || FindTask(parentId) is not { } parent)
        {
            return false;
        }

        var temp = new TaskDto(NextTempId(), title, null, null, parent.RoleIds, null, null, TaskStateDto.Planned, null, null, null,
            SortOrder: 0, TrashedAt: null, ParentId: parentId, ParentTitle: parent.Title, StepOrder: int.MaxValue);
        _tasks[temp.Id] = temp;

        var ok = await SaveAsync(async () =>
        {
            var step = await api.AddStepAsync(parentId, title);
            _tasks.Remove(temp.Id);
            _tasks[step.Id] = await api.GetTaskAsync(step.Id);
        });

        if (!ok && _tasks.Remove(temp.Id))
        {
            NotifyChanged();
        }

        return ok;
    }

    /// <summary>
    /// Проект, чьи шаги показывает дело: у занятия цели — проект его цели (G4, G6; null — у цели проекта нет), у остальных — само дело.
    /// </summary>
    public int? StepsHostOf(TaskDto task) =>
        task.GoalId is { } goalId ? FindGoal(goalId)?.ProjectId is { } projectId && projectId > 0 ? projectId : null
        : task.ParentId is null ? task.Id : null;

    /// <summary>
    /// Кого можно выбрать проектом (S6–S7, P1): открытые дела без родителя, не занятия целей — из ленты и показанной недели, по названию.
    /// </summary>
    public IReadOnlyList<TaskDto> ProjectCandidates() =>
        _tasks.Values
            .Where(t => t.Id > 0 && t.ParentId is null && t.GoalId is null
                        && t.State is TaskStateDto.Someday or TaskStateDto.Planned)
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Шаги цели — шаги её проекта (G1); проекта нет — пусто.</summary>
    public IReadOnlyList<TaskDto> StepsOfGoal(WeeklyGoalDto goal) =>
        goal.ProjectId is { } projectId ? CountedStepsOf(projectId) : [];

    /// <summary>
    /// «+ шаг» цели (в окне цели, в окне и строке её занятия): шаг — в проект цели; проекта нет — сервер создаёт его наверху «неразобранного»
    /// и выбирает в цели (G3). false — не сохранился.
    /// </summary>
    public async Task<bool> AddGoalStepAsync(int goalId, string title)
    {
        title = title.Trim();
        if (title.Length == 0 || goalId < 0)
        {
            return false;
        }

        return await SaveAsync(async () =>
        {
            var result = await api.AddGoalStepAsync(goalId, title);
            if (result.CreatedProject is { } project)
            {
                _tasks[project.Id] = project; // новый проект — в «неразобранном» ленты
            }

            _tasks[result.Step.Id] = result.Step;
            if (FindGoal(goalId) is not null)
            {
                ReplaceGoal(result.Goal);
            }
        });
    }

    /// <summary>
    /// После отметки: родитель отметился сам (все шаги сделаны, S12) — он обновляется на экране и плашка
    /// «Все шаги сделаны — «…» выполнено · вернуть»: «вернуть» снимает отметку с того, что отметилось само — родителя и/или занятия (R4;
    /// проект мог быть выполнен раньше — тогда отмечается только занятие).
    /// </summary>
    private void AfterParentAutoDone(TaskDto? parent, TaskDto? session = null)
    {
        if (parent is null && session is null)
        {
            return;
        }

        if (session is not null)
        {
            _tasks[session.Id] = session; // R4: последний шаг отметили из занятия — оно тоже выполнено
            SyncSession(session);
        }

        if (parent is null)
        {
            notifier.WithAction($"Все шаги сделаны — «{session!.Title}» выполнено", "вернуть",
                () => UncompleteAutoDoneParentAsync(null, session), Notifier.DefaultDuration);
            NotifyChanged();
            return;
        }

        if (parent.State == TaskStateDto.Done && parent.PlannedDate is null && parent.GoalId is null)
        {
            _tasks.Remove(parent.Id); // выполнено в ленте — из ленты уходит в «Выполненное»
        }
        else
        {
            _tasks[parent.Id] = parent;
            SyncSession(parent);
        }

        notifier.WithAction($"Все шаги сделаны — «{parent.Title}» выполнено", "вернуть",
            () => UncompleteAutoDoneParentAsync(parent, session), Notifier.DefaultDuration);
        NotifyChanged();
    }

    private async Task UncompleteAutoDoneParentAsync(TaskDto? parent, TaskDto? session)
    {
        try
        {
            await CommitPendingDeleteAsync();
            if (parent is not null)
            {
                var result = await api.SetTaskDoneAsync(parent.Id, false);
                _tasks[parent.Id] = result.Task;
                SyncSession(result.Task);
            }

            if (session is not null)
            {
                await api.SetTaskDoneAsync(session.Id, false);
            }
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        await ReloadAsync();
    }

    /// <summary>«Удалено «Ремонт» и 3 шага» — проект уходит в корзину вместе со своими шагами (S23).</summary>
    private static string StepsSuffix(int count) => count switch
    {
        0 => "",
        _ => $" и {count} " + (count % 10 == 1 && count % 100 != 11 ? "шаг"
            : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? "шага"
            : "шагов"),
    };
}
