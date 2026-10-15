using Planner.Shared;

namespace Planner.Client.Services;

// Дело на дело = шаг (docs/specs/next-batch.md, N2): бросили дело на середину строки другого дела — оно шаг этого дела,
// тот же переход, что выбор в поле «Проект» (S6–S10). Плашка «„Краска“ теперь шаг „Ремонт“ · Вернуть» — на прежнее место.
public partial class PlannerState
{
    /// <summary>Где дело было до броска — чтобы «Вернуть» поставило его туда же (N2.6).</summary>
    private sealed record StepOrigin(int? ParentId, int? CategoryId, int FeedIndex, DateOnly? Date, int DayIndex, bool WasSomeday);

    /// <summary>
    /// Можно ли бросить дело <paramref name="taskId"/> на строку <paramref name="targetId"/> (N2.4): цель — не шаг, не занятие цели,
    /// не само дело; у тащимого нет своих шагов и оно не проект.
    /// </summary>
    public bool CanBecomeStepOf(int taskId, int targetId)
    {
        if (taskId == targetId || FindTask(taskId) is not { } task || FindTask(targetId) is not { } target)
        {
            return false;
        }

        return target is { ParentId: null, GoalId: null } && target.State is TaskStateDto.Someday or TaskStateDto.Planned
            && !task.IsProject && StepsOf(taskId).Count == 0;
    }

    /// <summary>Бросок на середину строки дела (N2.1): шаг своего проекта — снять с дня (S20, N2.5), иначе — стать шагом.</summary>
    public async Task DropOnTaskAsync(int taskId, int targetId)
    {
        if (taskId < 0 || targetId < 0 || FindTask(taskId) is not { } task || FindTask(targetId) is not { } target)
        {
            return;
        }

        if (task.ParentId == targetId)
        {
            await MoveStepToListAsync(taskId, targetId, int.MaxValue);
            return;
        }

        if (!CanBecomeStepOf(taskId, targetId))
        {
            return;
        }

        var origin = new StepOrigin(
            task.ParentId,
            task.CategoryId,
            SomedayIn(task.CategoryId).ToList().FindIndex(t => t.Id == taskId),
            task.PlannedDate,
            task.PlannedDate is { } date ? TasksOn(date).Where(t => t.StartTime is null).ToList().FindIndex(t => t.Id == taskId) : -1,
            task.State == TaskStateDto.Someday);
        var targetBecomesProject = !target.IsProject;

        if (!await SaveAsync(async () => _tasks[taskId] = await api.SetParentAsync(taskId, targetId)))
        {
            return;
        }

        await ReloadAsync(); // мысль ушла из ленты, проект мог уйти в «🛠 Проекты» — проще перечитать

        var message = $"«{task.Title}» теперь шаг «{target.Title}»";
        Func<Task> undo = () => UndoMakeStepAsync(taskId, targetId, origin, targetBecomesProject);
        if (targetBecomesProject && target.State == TaskStateDto.Someday)
        {
            // Мысль, на которую бросили, стала проектом и ушла из ленты (N6.3)
            notifier.WithActions(message, "🛠 Проекты", () =>
            {
                RequestProjects(targetId);
                return Task.CompletedTask;
            }, "Вернуть", undo, UndoWindow, nearTrash: true);
        }
        else
        {
            notifier.WithAction(message, "Вернуть", undo, UndoWindow, nearTrash: origin.WasSomeday);
        }
    }

    /// <summary>«Вернуть» (N2.6): убрать из проекта и поставить на прежнее место; проект, появившийся от броска, — снова не проект.</summary>
    private async Task UndoMakeStepAsync(int taskId, int targetId, StepOrigin origin, bool targetBecameProject)
    {
        await SaveAsync(async () =>
        {
            await api.SetParentAsync(taskId, origin.ParentId);
            if (origin.WasSomeday)
            {
                await api.MoveTaskAsync(taskId, new SomedayTargetDto(origin.CategoryId, Math.Max(origin.FeedIndex, 0)));
            }
            else if (origin.Date is { } date)
            {
                await api.MoveTaskAsync(taskId, new DayTargetDto(date, origin.DayIndex < 0 ? int.MaxValue : origin.DayIndex));
            }

            if (targetBecameProject)
            {
                await api.SetProjectAsync(targetId, false);
            }
        });
        await ReloadAsync();
    }
}
