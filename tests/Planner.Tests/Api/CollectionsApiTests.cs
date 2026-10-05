using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Коллекции: привязка категорий ленты, ручная привязка важнее, шаги не попадают, полка и годы; шаги дела.</summary>
public class CollectionsApiTests : ApiTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private Task<HabitDto> CreateCollectionAsync(string name, IReadOnlyList<int>? categoryIds = null, int? yearGoal = null) =>
        PostAsync<HabitDto>("/api/habits",
            new SaveHabitRequest(name, "📚", null, 1, Kind: HabitKindDto.Collection, YearGoal: yearGoal, CategoryIds: categoryIds ?? []));

    private async Task<HabitDto> HabitAsync(int id) =>
        (await GetAsync<HabitsDto>($"/api/habits?today={Today:yyyy-MM-dd}")).Habits.Single(h => h.Id == id);

    private async Task<TaskDto> BookInPlanAsync(CategoryDto category, string title, bool done = true)
    {
        var task = await CreateTaskAsync(title, new SomedayTargetDto(category.Id, 0));
        var planned = await MoveTaskAsync(task.Id, new DayTargetDto(Today, 0));
        Assert.Equal(category.Id, planned.CategoryId); // исходная категория у дела в плане сохраняется
        return done ? await PutAsync<TaskDto>($"/api/tasks/{task.Id}/done", new SetDoneRequest(true)) : planned;
    }

    private Task<StepDto> AddStepAsync(int taskId, string title) =>
        PostAsync<StepDto>($"/api/tasks/{taskId}/steps", new StepRequest(title));

    [Fact]
    public async Task Collection_FromCategory_DoneTaskInPlanCounts_UncheckRemoves()
    {
        var category = await CreateCategoryAsync("Хочу прочитать");
        var books = await CreateCollectionAsync("Книги", [category.Id], yearGoal: 12);
        Assert.Equal((HabitKindDto.Collection, 12), (books.Kind, books.YearGoal!.Value));
        Assert.Equal([category.Id], books.CategoryIds);

        var book = await BookInPlanAsync(category, "«Дюна»");
        await BookInPlanAsync(category, "«Солярис»", done: false);

        var collection = (await HabitAsync(books.Id)).Collection!;
        Assert.Equal((1, 1, 12), (collection.Total, collection.ThisYear, collection.YearGoal!.Value));
        var entry = Assert.Single(collection.Entries);
        Assert.Equal(("«Дюна»", Today, 0, (int?)null), (entry.Title, entry.CompletedOn, entry.StepCount, entry.SpanDays));
        Assert.Equal([new CollectionYearDto(Today.Year, 1)], collection.Years);

        await PutAsync<TaskDto>($"/api/tasks/{book.Id}/done", new SetDoneRequest(false));
        Assert.Equal(0, (await HabitAsync(books.Id)).Collection!.Total);
    }

    [Fact]
    public async Task ManualHabit_WinsOverCategory()
    {
        var category = await CreateCategoryAsync("Хочу прочитать");
        var books = await CreateCollectionAsync("Книги", [category.Id]);
        var other = await CreateCollectionAsync("Подарки");
        var book = await BookInPlanAsync(category, "Книга в подарок");

        await PutAsync<TaskDto>($"/api/tasks/{book.Id}", new UpdateTaskRequest(book.Title, null, [], null, null, null, other.Id));

        Assert.Equal(0, (await HabitAsync(books.Id)).Collection!.Total);
        Assert.Equal(1, (await HabitAsync(other.Id)).Collection!.Total);
    }

    [Fact]
    public async Task Steps_OfTaskFromLinkedCategory_DoNotCount_ProjectShowsStepsAndSpan()
    {
        var category = await CreateCategoryAsync("Проекты");
        var projects = await CreateCollectionAsync("Проекты", [category.Id]);
        var project = await BookInPlanAsync(category, "Сарай", done: false);
        var first = await AddStepAsync(project.Id, "Доски");
        await AddStepAsync(project.Id, "Крыша");
        await PutAsync<StepDto>($"/api/steps/{first.Id}/done", new SetDoneRequest(true));

        // Шаг отмечен — в коллекции пусто: шаги туда не попадают
        Assert.Equal(0, (await HabitAsync(projects.Id)).Collection!.Total);

        await PutAsync<TaskDto>($"/api/tasks/{project.Id}/done", new SetDoneRequest(true));
        var entry = Assert.Single((await HabitAsync(projects.Id)).Collection!.Entries);
        Assert.Equal((2, 1), (entry.StepCount, entry.SpanDays!.Value)); // шаг в тот же день — «за 1 день»
    }

    [Fact]
    public async Task Steps_CrudOrder_NotInDayFeedOrTrash()
    {
        var task = await CreateInDayAsync("Ремонт", Today);
        var a = await AddStepAsync(task.Id, "Обои");
        var b = await AddStepAsync(task.Id, "Плинтус");
        await PutAsync<StepDto>($"/api/steps/{b.Id}", new StepRequest("Плинтус и пороги"));
        var done = await PutAsync<StepDto>($"/api/steps/{a.Id}/done", new SetDoneRequest(true));
        Assert.True(done.IsDone);

        var steps = await GetAsync<List<StepDto>>($"/api/tasks/{task.Id}/steps");
        Assert.Equal(["Обои", "Плинтус и пороги"], steps.Select(s => s.Title));
        Assert.All(steps, s => Assert.Equal(task.Id, s.ParentId));

        var week = await GetWeekAsync(Today);
        Assert.Equal(["Ремонт"], week.Tasks.Select(t => t.Title));
        Assert.Empty((await GetBoardAsync()).SomedayTasks);

        (await Client.DeleteAsync($"/api/steps/{a.Id}")).EnsureSuccessStatusCode();
        Assert.Single(await GetAsync<List<StepDto>>($"/api/tasks/{task.Id}/steps"));
        Assert.Equal(0, (await GetBoardAsync()).TrashCount);

        // У шага своих шагов нет
        var nested = await Client.PostAsJsonAsync($"/api/tasks/{b.Id}/steps", new StepRequest("x"));
        Assert.Equal(HttpStatusCode.BadRequest, nested.StatusCode);
    }

    [Fact]
    public async Task Steps_DeletedForeverWithTheirTask()
    {
        var task = await CreateInDayAsync("Ремонт", Today);
        await AddStepAsync(task.Id, "Обои");
        (await Client.DeleteAsync($"/api/tasks/{task.Id}")).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/trash/{task.Id}")).EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"/api/tasks/{task.Id}/steps");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Category_BelongsToOneCollection_MovesToTheNewOne()
    {
        var category = await CreateCategoryAsync("Хочу прочитать");
        var first = await CreateCollectionAsync("Книги", [category.Id]);
        var second = await CreateCollectionAsync("Чтение 2026", [category.Id]);

        Assert.Empty((await HabitAsync(first.Id)).CategoryIds!);
        Assert.Equal([category.Id], (await HabitAsync(second.Id)).CategoryIds);
    }

    [Fact]
    public async Task KindSwitch_KeepsHistory_HabitKindIgnoresCategoryLinks()
    {
        var category = await CreateCategoryAsync("Хочу прочитать");
        var books = await CreateCollectionAsync("Книги", [category.Id]);
        await BookInPlanAsync(category, "«Дюна»");

        // Стала привычкой: привязка категории хранится, но не действует
        var habit = await PutAsync<HabitDto>($"/api/habits/{books.Id}", new SaveHabitRequest("Книги", "📚", null, 1, Kind: HabitKindDto.Habit));
        Assert.Equal([category.Id], habit.CategoryIds);
        Assert.Equal(0, habit.Stats.Total);

        // Снова коллекция — запись на месте
        var again = await PutAsync<HabitDto>($"/api/habits/{books.Id}", new SaveHabitRequest("Книги", "📚", null, 1, Kind: HabitKindDto.Collection));
        Assert.Equal(1, again.Collection!.Total);
    }

    [Fact]
    public async Task Collection_NoPlusOne()
    {
        var books = await CreateCollectionAsync("Книги");

        var response = await Client.PostAsJsonAsync($"/api/habits/{books.Id}/plus-one", new PlusOneRequest(Today));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletedCategory_LinkGone()
    {
        var category = await CreateCategoryAsync("Хочу прочитать");
        var books = await CreateCollectionAsync("Книги", [category.Id]);

        (await Client.DeleteAsync($"/api/categories/{category.Id}?keepTasks=true")).EnsureSuccessStatusCode();

        Assert.Empty((await HabitAsync(books.Id)).CategoryIds!);
    }
}
