namespace Planner.Core.Habits;

/// <summary>
/// Скрепка привычки — выполненное занятие цели её серии (и выполненное занятие удалённой цели серии; корзина не считается), как его отдаёт
/// хранилище. День скрепки: день дела в плане, иначе неделя его цели (занятие без дня, отмеченное галочкой цели).
/// </summary>
/// <param name="SeriesId">Серия цели — привычка.</param>
/// <param name="GoalWeek">Понедельник недели цели.</param>
public sealed record HabitClip(int SeriesId, DateOnly? PlannedDate, DateOnly GoalWeek)
{
    public DateOnly Day => PlannedDate ?? GoalWeek;
}

/// <summary>Норма недели привычки: «раз в неделю» цели серии на этой неделе.</summary>
public sealed record HabitWeekNorm(int SeriesId, DateOnly WeekStart, int TimesPerWeek);
