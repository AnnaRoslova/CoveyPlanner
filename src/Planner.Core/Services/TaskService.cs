using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Time;

namespace Planner.Core.Services;

/// <summary>Сценарии работы с делами: создать, изменить, перенести, выполнить, корзина.</summary>
public class TaskService(
    ITaskRepository tasks,
    IRoleRepository roles,
    GoalSessions sessions,
    GoalSeriesService series,
    TaskPlacer placer,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Новое дело: в ленту (обычно наверх категории, Index = 0) или в день.</summary>
    public async Task<TaskItem> CreateAsync(string title, TaskTarget target, CancellationToken ct = default)
    {
        var task = target is SomedayTarget someday
            ? TaskItem.CreateSomeday(title, someday.CategoryId, Now)
            : TaskItem.CreatePlanned(title, Now);
        await placer.PlaceAsync(task, target, ct);
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// Изменить дело из окна редактирования. startTime / durationMinutes = null — убрать; roleIds пусто — без ролей.
    /// У занятия целей роли и квадрант всегда от целей — присланные значения не применяются.
    /// </summary>
    public async Task<TaskItem> UpdateAsync(
        int id, string title, string? notes, IReadOnlyCollection<int> roleIds, Quadrant? quadrant, TimeOnly? startTime,
        int? durationMinutes, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        // У занятия целей роли — роли всех его целей, квадрант — от главной (так же решает клиент)
        if (task.IsSession)
        {
            await sessions.LoadGoalsOfAsync(task, ct);
            task.RefreshFromGoals();
            roleIds = task.RoleIds;
            quadrant = task.Quadrant;
        }

        foreach (var roleId in roleIds.Distinct())
        {
            if (await roles.FindAsync(roleId, ct) is null)
            {
                throw new NotFoundException("Роль", roleId);
            }
        }

        if (quadrant is { } q && !Enum.IsDefined(q))
        {
            throw new DomainException("Квадрант должен быть от 1 до 4.");
        }

        task.Rename(title);
        task.SetNotes(notes);
        task.SetDuration(durationMinutes);
        task.SetRoles(roleIds);
        task.Quadrant = quadrant;

        var hadTime = task.StartTime is not null;
        task.SetTime(startTime);
        if (hadTime && startTime is null)
        {
            // Время убрали — дело встаёт в конец ручного списка дня
            await placer.MoveToEndOfDayAsync(task, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// Перенос дела (перетаскиванием или датой в окне). При возврате в ленту связь с целью разрывается.
    /// Занятие цели, перенесённое на другую неделю, остаётся «камнем»: становится занятием той же цели на той неделе
    /// (цели её серии, а если такой нет — новой копии цели, 1 раз в неделю); у прежней цели прогресс уменьшается.
    /// </summary>
    public async Task<TaskItem> MoveAsync(int id, TaskTarget target, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);

        // Занятие целей в день: каждая его цель — на неделе того дня (эта же или та же на другой неделе); в день, где
        // у какой-то из них уже есть занятие, нельзя (если не разрешено несколько в день) — проверяем, пока ничего не сдвинули
        var transfers = new List<(WeeklyGoal From, WeeklyGoal To)>();
        if (target is DayTarget day && task.IsSession)
        {
            var week = Week.StartOf(day.Date);
            foreach (var goal in await sessions.LoadGoalsOfAsync(task, ct))
            {
                var to = goal.WeekStart == week ? goal : await series.GoalForWeekAsync(goal, week, ct);
                if (task.PlannedDate != day.Date) // порядок внутри своего дня — можно и у старых дублей
                {
                    to.EnsureDayIsFree(day.Date, except: task);
                }

                if (to != goal)
                {
                    transfers.Add((goal, to));
                }
            }
        }

        await placer.PlaceAsync(task, target, ct);

        if (target is SomedayTarget)
        {
            task.DetachFromGoals();
        }

        // На другую неделю — каждая связь переходит к той же цели той недели (порядок связей сохраняется)
        foreach (var (from, to) in transfers)
        {
            from.RemoveSession(task);
            to.AddSession(task);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    public async Task<TaskItem> SetDoneAsync(int id, bool isDone, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        if (isDone)
        {
            task.Complete(Now);
        }
        else
        {
            task.Uncomplete();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// В корзину. Занятие цели остаётся связанным с ней, но в прогресс не входит;
    /// вернули из корзины — снова засчитывается.
    /// </summary>
    public async Task TrashAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        task.Trash(Now);

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Вернуть из корзины на прежнее место. Занятие цели без дня снова висит на цели.
    /// Дело ленты возвращается в свою категорию, а если её нет — в «неразобранное».
    /// </summary>
    public async Task<TaskItem> RestoreAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        task.Restore();

        if (task.IsOnDay)
        {
            await placer.MoveToEndOfDayAsync(task, ct);
        }
        else if (!task.IsSession)
        {
            await placer.ReturnToSomedayAsync(task, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return task;
    }

    public async Task DeleteForeverAsync(int id, CancellationToken ct = default)
    {
        var task = await GetAsync(id, ct);
        if (task.State != TaskState.Trashed)
        {
            throw new DomainException("Удалить навсегда можно только дело из корзины.");
        }

        tasks.Remove(task);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task EmptyTrashAsync(CancellationToken ct = default)
    {
        foreach (var task in await tasks.GetTrashedAsync(ct))
        {
            tasks.Remove(task);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<TaskItem> GetAsync(int id, CancellationToken ct) =>
        await tasks.FindAsync(id, ct) ?? throw new NotFoundException("Дело", id);
}
