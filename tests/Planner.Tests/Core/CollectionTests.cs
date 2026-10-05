using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Errors;
using Planner.Core.Habits;
using Planner.Core.Services;

namespace Planner.Tests.Core;

/// <summary>Коллекции: чья скрепка (ручная привязка важнее категории, шаги не считаются), счёт, годы, длительность проекта.</summary>
public class CollectionTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly DateTime Now = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);

    // Категория 5 («Хочу прочитать») привязана к коллекции 10 («Книги»)
    private static readonly IReadOnlyDictionary<int, int> Links = new Dictionary<int, int> { [5] = 10 };

    private static HabitClipSource Clip(int? habitId = null, int? categoryId = null, int? parentId = null) =>
        new(1, "Дело", parentId, habitId, categoryId, Today, null, Now);

    // ---------- Чья скрепка ----------

    [Fact]
    public void Attribution_TaskFromLinkedCategory_GoesToCollection()
    {
        Assert.Equal(10, HabitAttribution.HabitOf(Clip(categoryId: 5), Links));
    }

    [Fact]
    public void Attribution_ManualHabit_WinsOverCategory()
    {
        Assert.Equal(3, HabitAttribution.HabitOf(Clip(habitId: 3, categoryId: 5), Links));
    }

    [Fact]
    public void Attribution_OtherCategoryOrNone_Nowhere()
    {
        Assert.Null(HabitAttribution.HabitOf(Clip(categoryId: 6), Links));
        Assert.Null(HabitAttribution.HabitOf(Clip(), Links));
    }

    [Fact]
    public void Attribution_Steps_NeverCount_EvenFromLinkedCategoryOrWithHabit()
    {
        Assert.Null(HabitAttribution.HabitOf(Clip(categoryId: 5, parentId: 7), Links));
        Assert.Null(HabitAttribution.HabitOf(Clip(habitId: 3, parentId: 7), Links));
    }

    [Fact]
    public void CollectionOfCategory_OnlyCollections_HabitKindLinksIgnored()
    {
        var books = Habit.Create("Книги", "📚", "#a9cde8", 1, 30, 0, Now);
        books.SetKind(HabitKind.Collection);
        books.SetCategories([5, 6]);
        var habit = Habit.Create("Чтение", null, "#a9cde8", 3, 30, 0, Now);
        habit.SetCategories([7]); // привычка — привязка хранится, но не работает

        var map = HabitService.CollectionOfCategory([books, habit]);

        Assert.Equal([5, 6], map.Keys.Order());
    }

    [Fact]
    public void PlannedTask_KeepsItsFeedCategory()
    {
        // Дело вынули из ленты в план — категория остаётся (по ней дело попадает в коллекцию)
        var task = TaskItem.CreateSomeday("«Мастер и Маргарита»", 5, Now);
        task.PlanForDay(Today);
        task.Complete(Now);

        Assert.Equal(5, task.CategoryId);
    }

    // ---------- Счёт и годы ----------

    [Fact]
    public void Stats_TotalIncludesStartCount_ThisYearAndGoal_YearsAscending()
    {
        CollectionItem[] items =
        [
            new(1, "А", new(2025, 3, 1)), new(2, "Б", new(2025, 12, 31)), new(3, "В", new(2026, 1, 1)), new(4, "Г", new(2026, 9, 9)),
        ];

        var stats = CollectionStatsCalculator.Calculate(items, startCount: 10, yearGoal: 12, Today);

        Assert.Equal((14, 2, 12), (stats.Total, stats.ThisYear, stats.YearGoal!.Value));
        Assert.Equal([new(2025, 2), new(2026, 2)], stats.Years);
    }

    [Fact]
    public void Stats_EntriesNewestFirst()
    {
        CollectionItem[] items = [new(1, "старое", new(2026, 1, 5)), new(2, "новое", new(2026, 9, 1)), new(3, "тот же день, позже", new(2026, 9, 1))];

        var stats = CollectionStatsCalculator.Calculate(items, 0, null, Today);

        Assert.Equal(["тот же день, позже", "новое", "старое"], stats.Entries.Select(e => e.Title));
        Assert.Null(stats.YearGoal);
    }

    [Fact]
    public void Stats_Empty()
    {
        var stats = CollectionStatsCalculator.Calculate([], 0, 24, Today);

        Assert.Equal((0, 0), (stats.Total, stats.ThisYear));
        Assert.Empty(stats.Years);
        Assert.Empty(stats.Entries);
    }

    // ---------- Длительность проекта ----------

    [Fact]
    public void Span_FromFirstDoneStepToCompletion_Inclusive()
    {
        var stats = CollectionStatsCalculator.Calculate(
            [new CollectionItem(1, "Сарай", new(2026, 9, 30), StepCount: 14, FirstStepOn: new(2026, 8, 27))], 0, null, Today);

        var entry = Assert.Single(stats.Entries);
        Assert.Equal((14, 35), (entry.StepCount, entry.SpanDays!.Value)); // 27 августа … 30 сентября — 5 недель
    }

    [Fact]
    public void Span_StepsSameDay_OneDay()
    {
        var stats = CollectionStatsCalculator.Calculate([new CollectionItem(1, "Полка", Today, 3, Today)], 0, null, Today);

        Assert.Equal(1, stats.Entries[0].SpanDays);
    }

    [Fact]
    public void Span_NoDoneSteps_NoSpan_StepCountStillShown()
    {
        var stats = CollectionStatsCalculator.Calculate([new CollectionItem(1, "Сайт", Today, 4)], 0, null, Today);

        Assert.Equal((4, (int?)null), (stats.Entries[0].StepCount, stats.Entries[0].SpanDays));
    }

    [Fact]
    public void Span_StepMarkedAfterCompletion_StillOneDay()
    {
        var stats = CollectionStatsCalculator.Calculate([new CollectionItem(1, "Х", Today, 2, Today.AddDays(3))], 0, null, Today);

        Assert.Equal(1, stats.Entries[0].SpanDays);
    }

    [Fact]
    public void CompletedOn_IsLocalDayOfCompletion_NotPlannedDay()
    {
        var clip = new HabitClipSource(1, "Книга", null, null, 5, new DateOnly(2026, 10, 5), null, new DateTime(2026, 10, 7, 21, 30, 0));

        Assert.Equal(new DateOnly(2026, 10, 7), clip.CompletedOn(TimeZoneInfo.Utc));
        Assert.Equal(new DateOnly(2026, 10, 5), clip.Day(TimeZoneInfo.Utc)); // для привычки — день в плане
    }

    // ---------- Копилка: вид, цель, категории ----------

    [Fact]
    public void Habit_KindYearGoalCategories()
    {
        var habit = Habit.Create("Книги", "📚", "#a9cde8", 1, 30, 0, Now);
        Assert.Equal(HabitKind.Habit, habit.Kind);

        habit.SetKind(HabitKind.Collection);
        habit.SetYearGoal(24);
        habit.SetCategories([5, 5, 6]);
        Assert.Equal((true, 24), (habit.IsCollection, habit.YearGoal!.Value));
        Assert.Equal([5, 6], habit.CategoryIds);

        habit.RemoveCategory(5);
        Assert.Equal([6], habit.CategoryIds);
        Assert.Throws<DomainException>(() => habit.SetYearGoal(0));
    }

    // ---------- Шаги ----------

    [Fact]
    public void Step_BelongsToParent_NoDayNoCategory_NoNesting()
    {
        var parent = TaskItem.CreateSomeday("Сарай", 5, Now);
        var step = TaskItem.CreateStep(parent, "Купить доски", Now);

        Assert.True(step.IsStep);
        Assert.Equal((TaskState.Planned, (int?)null, (DateOnly?)null), (step.State, step.CategoryId, step.PlannedDate));
        Assert.Throws<DomainException>(() => TaskItem.CreateStep(step, "Шаг шага", Now));

        step.Complete(Now);
        Assert.Equal(TaskState.Done, step.State);
    }

    [Fact]
    public void Step_TrashedParent_NoNewSteps()
    {
        var parent = TaskItem.CreatePlanned("Сарай", Now);
        parent.Trash(Now);

        Assert.Throws<DomainException>(() => TaskItem.CreateStep(parent, "Шаг", Now));
    }
}
