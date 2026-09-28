using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Entities;

/// <summary>
/// Дело. Одна и та же сущность живёт в ленте «Когда-нибудь», в плане недели и в корзине —
/// при переносе меняются только State и поля даты/времени, а не создаётся копия.
/// CategoryId сохраняется и в плане, чтобы при возврате в ленту дело попало в свою категорию.
/// </summary>
public class TaskItem : ISortable
{
    // Для EF Core
    private TaskItem()
    {
    }

    public int Id { get; private set; }

    public string Title { get; private set; } = "";

    public string? Notes { get; private set; }

    /// <summary>Категория ленты. null — дело создано сразу в плане (при возврате попадёт во «Входящие»).</summary>
    public int? CategoryId { get; private set; }

    public int? RoleId { get; set; }

    public Quadrant? Quadrant { get; set; }

    public TaskState State { get; private set; }

    /// <summary>Состояние до попадания в корзину — для возврата.</summary>
    public TaskState? PreviousState { get; private set; }

    /// <summary>День плана. null у запланированного дела — оно висит на цели недели без дня.</summary>
    public DateOnly? PlannedDate { get; private set; }

    /// <summary>Начало в сетке часов. null — дело в «Приоритетах дня».</summary>
    public TimeOnly? StartTime { get; private set; }

    public int? DurationMinutes { get; private set; }

    /// <summary>Порядок внутри категории ленты или внутри «Приоритетов дня».</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime? TrashedAt { get; private set; }

    public bool IsInPlan => State is TaskState.Planned or TaskState.Done;

    /// <summary>Дело в «Приоритетах дня» (есть день, нет времени).</summary>
    public bool IsDayPriority => IsInPlan && PlannedDate is not null && StartTime is null;

    /// <summary>Дело в сетке часов.</summary>
    public bool IsScheduled => IsInPlan && StartTime is not null;

    /// <summary>Новое дело в ленте.</summary>
    public static TaskItem CreateSomeday(string title, int categoryId, DateTime now) => new()
    {
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название дела"),
        CategoryId = categoryId,
        State = TaskState.Someday,
        CreatedAt = now,
    };

    /// <summary>Новое дело сразу в плане (без категории). Размещение — через PlanForDay/ScheduleAt/PlanForWeek.</summary>
    public static TaskItem CreatePlanned(string title, DateTime now) => new()
    {
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название дела"),
        State = TaskState.Planned,
        CreatedAt = now,
    };

    public void Rename(string title) =>
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название дела");

    public void SetNotes(string? notes) =>
        Notes = TextRules.Optional(notes, TextRules.NotesMaxLength, "Заметки");

    /// <summary>Вернуть в ленту, в указанную категорию. Дата, время и отметка «выполнено» сбрасываются.</summary>
    public void MoveToSomeday(int categoryId)
    {
        EnsureNotTrashed();
        State = TaskState.Someday;
        CategoryId = categoryId;
        CompletedAt = null;
        ClearSchedule();
    }

    /// <summary>В «Приоритеты дня». Выполненное дело остаётся выполненным.</summary>
    public void PlanForDay(DateOnly date)
    {
        EnsureNotTrashed();
        MarkPlannedIfSomeday();
        PlannedDate = date;
        StartTime = null;
        DurationMinutes = null;
    }

    /// <summary>В сетку часов. Время и длительность должны быть уже выровнены по настройкам сетки.</summary>
    public void ScheduleAt(DateOnly date, TimeOnly start, int durationMinutes)
    {
        if (durationMinutes <= 0)
        {
            throw new DomainException("Длительность должна быть больше нуля.");
        }

        EnsureNotTrashed();
        MarkPlannedIfSomeday();
        PlannedDate = date;
        StartTime = start;
        DurationMinutes = durationMinutes;
    }

    /// <summary>Запланировано на неделю без конкретного дня (дело привязано к цели).</summary>
    public void PlanForWeek()
    {
        EnsureNotTrashed();
        MarkPlannedIfSomeday();
        ClearSchedule();
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

    private void ClearSchedule()
    {
        PlannedDate = null;
        StartTime = null;
        DurationMinutes = null;
    }

    private void EnsureNotTrashed()
    {
        if (State == TaskState.Trashed)
        {
            throw new DomainException("Дело в корзине — сначала верните его.");
        }
    }
}
