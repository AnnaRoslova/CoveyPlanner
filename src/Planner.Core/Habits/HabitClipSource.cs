namespace Planner.Core.Habits;

/// <summary>
/// Выполненное дело — возможная скрепка — в том виде, как его отдаёт хранилище. Чья это скрепка, решает
/// <see cref="HabitAttribution"/>. День скрепки привычки: день дела в плане, иначе неделя его цели (занятие без дня,
/// отмеченное галочкой цели), иначе день, когда отметили. Запись коллекции датируется днём завершения.
/// </summary>
/// <param name="ParentId">Дело — шаг другого дела (такие в копилки не попадают).</param>
/// <param name="HabitId">Копилка, выбранная вручную (или от цели).</param>
/// <param name="CategoryId">Категория ленты, откуда дело (сохраняется и у дела в плане).</param>
/// <param name="StepCount">Сколько у дела шагов.</param>
/// <param name="FirstStepDoneAt">Когда отмечен первый шаг (UTC); null — ни одного.</param>
public sealed record HabitClipSource(
    int TaskId,
    string Title,
    int? ParentId,
    int? HabitId,
    int? CategoryId,
    DateOnly? PlannedDate,
    DateOnly? GoalWeek,
    DateTime? CompletedAt,
    int StepCount = 0,
    DateTime? FirstStepDoneAt = null)
{
    /// <param name="zone">Часовой пояс пользователя: CompletedAt хранится в UTC.</param>
    public DateOnly Day(TimeZoneInfo zone) => PlannedDate ?? GoalWeek ?? Local(CompletedAt, zone) ?? DateOnly.MinValue;

    /// <summary>День завершения (для коллекции): когда отметили, иначе день в плане.</summary>
    public DateOnly CompletedOn(TimeZoneInfo zone) => Local(CompletedAt, zone) ?? PlannedDate ?? GoalWeek ?? DateOnly.MinValue;

    public DateOnly? FirstStepOn(TimeZoneInfo zone) => Local(FirstStepDoneAt, zone);

    private static DateOnly? Local(DateTime? utc, TimeZoneInfo zone) =>
        utc is { } t ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t, DateTimeKind.Utc), zone)) : null;
}

/// <summary>Чья скрепка выполненное дело.</summary>
public static class HabitAttribution
{
    /// <summary>
    /// Копилка дела: выбранная вручную (или от цели) важнее категории; иначе — коллекция, к которой привязана категория дела.
    /// Шаги не попадают никуда — только дела верхнего уровня.
    /// </summary>
    /// <param name="collectionOfCategory">Категория ленты → коллекция (только у копилок вида «коллекция»).</param>
    public static int? HabitOf(HabitClipSource clip, IReadOnlyDictionary<int, int> collectionOfCategory) =>
        clip.ParentId is not null ? null
        : clip.HabitId ?? (clip.CategoryId is { } category && collectionOfCategory.TryGetValue(category, out var habitId) ? habitId : null);
}
