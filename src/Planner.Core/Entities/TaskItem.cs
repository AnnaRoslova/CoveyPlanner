using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Entities;

/// <summary>
/// Дело. Одна и та же сущность живёт в ленте «Когда-нибудь», в плане недели и в корзине —
/// при переносе меняются только State и дата, а не создаётся копия.
/// CategoryId сохраняется и в плане, чтобы при возврате в ленту дело попало в свою категорию.
/// Дело ленты без категории — «неразобранное»: мысль, записанная сразу, без выбора категории.
/// </summary>
public class TaskItem : ISortable
{
    private readonly List<TaskRole> _roles = [];

    // Для EF Core
    private TaskItem()
    {
    }

    public int Id { get; private set; }

    public string Title { get; private set; } = "";

    public string? Notes { get; private set; }

    /// <summary>Категория ленты. null — в ленте это «неразобранное»; в плане — дело создано сразу там (или категорию удалили).</summary>
    public int? CategoryId { get; private set; }

    /// <summary>Роли дела (может быть несколько). У занятия цели — ровно одна, роль цели.</summary>
    public IReadOnlyCollection<TaskRole> Roles => _roles;

    /// <summary>Id ролей по возрастанию.</summary>
    public IReadOnlyList<int> RoleIds => _roles.Select(r => r.RoleId).Order().ToList();

    /// <summary>Цель недели, занятием которой является дело («камень»). У цели может быть несколько занятий.</summary>
    public int? GoalId { get; private set; }

    public Quadrant? Quadrant { get; set; }

    public TaskState State { get; private set; }

    /// <summary>Состояние до попадания в корзину — для возврата.</summary>
    public TaskState? PreviousState { get; private set; }

    /// <summary>День плана. null у запланированного дела — оно висит на цели недели без дня.</summary>
    public DateOnly? PlannedDate { get; private set; }

    /// <summary>Необязательная метка времени («15:00 Стоматолог»). Только у дела, стоящего в дне.</summary>
    public TimeOnly? StartTime { get; private set; }

    /// <summary>Необязательная примерная длительность («~30 мин»), в минутах. Расписания по ней нет.</summary>
    public int? DurationMinutes { get; private set; }

    /// <summary>
    /// Ручной порядок внутри категории ленты или внутри дня.
    /// В дне важен только для дел без времени: дела со временем идут первыми, по времени.
    /// </summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime? TrashedAt { get; private set; }

    public bool IsInPlan => State is TaskState.Planned or TaskState.Done;

    /// <summary>Дело стоит в конкретном дне.</summary>
    public bool IsOnDay => IsInPlan && PlannedDate is not null;

    /// <summary>Новое дело в ленте; categoryId = null — в «неразобранное».</summary>
    public static TaskItem CreateSomeday(string title, int? categoryId, DateTime now) => new()
    {
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название дела"),
        CategoryId = categoryId,
        State = TaskState.Someday,
        CreatedAt = now,
    };

    /// <summary>Новое дело сразу в плане (без категории). Размещение — через PlanForDay/PlanForWeek.</summary>
    public static TaskItem CreatePlanned(string title, DateTime now) => new()
    {
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название дела"),
        State = TaskState.Planned,
        CreatedAt = now,
    };

    /// <summary>
    /// Задать роли дела (повторы не считаются). Уже стоящие роли остаются теми же записями —
    /// EF не пытается удалить и тут же добавить строку с тем же ключом.
    /// </summary>
    public void SetRoles(IEnumerable<int> roleIds)
    {
        var wanted = roleIds.ToHashSet();
        _roles.RemoveAll(r => !wanted.Contains(r.RoleId));
        foreach (var roleId in wanted.Where(id => _roles.All(r => r.RoleId != id)))
        {
            _roles.Add(new TaskRole(roleId));
        }
    }

    /// <summary>Отвязать от цели: дело вернули в ленту или цель удалили.</summary>
    public void DetachFromGoal() => GoalId = null;

    public void Rename(string title) =>
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название дела");

    public void SetNotes(string? notes) =>
        Notes = TextRules.Optional(notes, TextRules.NotesMaxLength, "Заметки");

    public void SetDuration(int? minutes) => DurationMinutes = TextRules.Duration(minutes);

    /// <summary>Поставить или убрать метку времени. Время бывает только у дела в дне.</summary>
    public void SetTime(TimeOnly? time)
    {
        if (time is not null && !IsOnDay)
        {
            throw new DomainException("Время можно указать только у дела, запланированного на день.");
        }

        // Секунды не нужны: метка вида «15:00»
        StartTime = time is { } t ? new TimeOnly(t.Hour, t.Minute) : null;
    }

    /// <summary>Вернуть в ленту, в указанную категорию (null — в «неразобранное»). Дата, время и отметка «выполнено» сбрасываются.</summary>
    public void MoveToSomeday(int? categoryId)
    {
        EnsureNotTrashed();
        State = TaskState.Someday;
        CategoryId = categoryId;
        CompletedAt = null;
        PlannedDate = null;
        StartTime = null;
    }

    /// <summary>В день. Время (если было) сохраняется; выполненное дело остаётся выполненным.</summary>
    public void PlanForDay(DateOnly date)
    {
        EnsureNotTrashed();
        MarkPlannedIfSomeday();
        PlannedDate = date;
    }

    /// <summary>Запланировано на неделю без конкретного дня (дело привязано к цели).</summary>
    public void PlanForWeek()
    {
        EnsureNotTrashed();
        MarkPlannedIfSomeday();
        PlannedDate = null;
        StartTime = null;
    }

    public void Complete(DateTime now)
    {
        if (!IsInPlan)
        {
            throw new DomainException("Отметить выполненным можно только запланированное дело.");
        }

        if (State != TaskState.Done)
        {
            State = TaskState.Done;
            CompletedAt = now;
        }
    }

    public void Uncomplete()
    {
        if (State == TaskState.Done)
        {
            State = TaskState.Planned;
            CompletedAt = null;
        }
    }

    /// <summary>В корзину. Место в плане/ленте запоминается для возврата.</summary>
    public void Trash(DateTime now)
    {
        if (State == TaskState.Trashed)
        {
            return;
        }

        PreviousState = State;
        State = TaskState.Trashed;
        TrashedAt = now;
    }

    /// <summary>Вернуть из корзины туда, где дело было.</summary>
    public void Restore()
    {
        if (State != TaskState.Trashed)
        {
            throw new DomainException("Дело не в корзине.");
        }

        State = PreviousState ?? TaskState.Someday;
        PreviousState = null;
        TrashedAt = null;
    }

    private void MarkPlannedIfSomeday()
    {
        if (State == TaskState.Someday)
        {
            State = TaskState.Planned;
        }
    }

    private void EnsureNotTrashed()
    {
        if (State == TaskState.Trashed)
        {
            throw new DomainException("Дело в корзине — сначала верните его.");
        }
    }
}
