namespace Planner.Core.Habits;

/// <summary>
/// Скрепка — выполненное дело копилки — в том виде, как её отдаёт хранилище. День скрепки: день дела в плане,
/// иначе неделя его цели (занятие без дня, отмеченное галочкой цели), иначе день, когда отметили.
/// </summary>
public sealed record HabitClipSource(int HabitId, DateOnly? PlannedDate, DateOnly? GoalWeek, DateTime? CompletedAt)
{
    /// <param name="zone">Часовой пояс пользователя: CompletedAt хранится в UTC.</param>
    public DateOnly Day(TimeZoneInfo zone) =>
        PlannedDate
        ?? GoalWeek
        ?? (CompletedAt is { } done ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(done, zone)) : DateOnly.MinValue);
}
