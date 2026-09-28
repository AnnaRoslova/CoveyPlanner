using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Sorting;
using Planner.Core.Time;

namespace Planner.Core.Entities;

/// <summary>
/// Цель роли на неделю («большой камень»). Может быть связана с делом (TaskId):
/// дело создаётся, когда цель перетаскивают в день, или цель создаётся из дела ленты.
/// Название и отметка «выполнено» у цели и дела синхронизируются.
/// </summary>
public class WeeklyGoal : ISortable
{
    private WeeklyGoal()
    {
    }

    public int Id { get; private set; }

    public int RoleId { get; private set; }

    /// <summary>Понедельник недели.</summary>
    public DateOnly WeekStart { get; private set; }

    public string Title { get; private set; } = "";

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }

    public int? TaskId { get; private set; }

    public static WeeklyGoal Create(int roleId, DateOnly weekStart, string title)
    {
        if (!Week.IsWeekStart(weekStart))
        {
            throw new DomainException("Неделя должна начинаться с понедельника.");
        }

        return new WeeklyGoal
        {
            RoleId = roleId,
            WeekStart = weekStart,
            Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название цели"),
        };
    }

    public void Rename(string title) =>
        Title = TextRules.Required(title, TextRules.TitleMaxLength, "Название цели");

    public void LinkTask(TaskItem task)
    {
        TaskId = task.Id;
        IsDone = task.State == TaskState.Done;
    }

    public void UnlinkTask() => TaskId = null;
}
