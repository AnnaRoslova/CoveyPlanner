using Planner.Core.Enums;

namespace Planner.Core.Entities;

/// <summary>
/// Запись истории пропусков (docs/specs/skips.md, K2): когда, какое дело, какой пропуск и почему. Остаётся, даже если дело потом
/// перенесли (шаг «не сделала» вернулся в список проекта, «не сделала во вторник — перенесла на среду»); отмена пропуска удаляет свою запись.
/// </summary>
public class TaskSkip
{
    private TaskSkip()
    {
    }

    public int Id { get; private set; }

    public int TaskId { get; private set; }

    public SkipKind Kind { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>День дела; у дела «на неделе» — понедельник недели.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Цель, чьим занятием дело было в момент пропуска.</summary>
    public int? GoalId { get; private set; }

    public DateTime RecordedAt { get; private set; }

    public static TaskSkip Record(TaskItem task, SkipKind kind, string? reason, DateOnly date, DateTime now) => new()
    {
        TaskId = task.Id,
        Kind = kind,
        Reason = TextRules.Optional(reason, TextRules.SkipReasonMaxLength, "Причина"),
        Date = date,
        GoalId = task.GoalId,
        RecordedAt = now,
    };
}
