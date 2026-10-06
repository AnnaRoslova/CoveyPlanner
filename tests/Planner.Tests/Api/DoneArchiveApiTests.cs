using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Отметка в ленте (D) и «Выполненное» (V): только дела из ленты, по категориям, счёт по годам, снятие отметки.</summary>
public class DoneArchiveApiTests : ApiTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private Task<List<DoneGroupDto>> DoneAsync() => GetAsync<List<DoneGroupDto>>("/api/done");

    private Task<TaskDto> SetDoneAsync(int id, bool isDone = true) =>
        SetTaskDoneAsync(id, isDone);

    private Task<TaskDto> ThoughtAsync(string title, int? categoryId, int index = int.MaxValue) =>
        CreateTaskAsync(title, new SomedayTargetDto(categoryId, index));

    [Fact]
    public async Task V1_V2_K15_K21_OnlyTasksThatWereInFeed()
    {
        var books = await CreateCategoryAsync("Книги", "📚");
        var inFeed = await ThoughtAsync("Дюна", books.Id);
        var toDay = await ThoughtAsync("Солярис", books.Id);
        var weekOnly = await CreateInDayAsync("Позвонить");
        var weekThenFeed = await CreateInDayAsync("Купить лампу");

        await SetDoneAsync(inFeed.Id);
        await MoveTaskAsync(toDay.Id, new DayTargetDto(Monday, 0));
        await SetDoneAsync(toDay.Id);
        await SetDoneAsync(weekOnly.Id);
        await MoveTaskAsync(weekThenFeed.Id, new SomedayTargetDto(null, 0));
        await SetDoneAsync(weekThenFeed.Id);

        var groups = await DoneAsync();
        Assert.Equal(["Книги", "без категории"], groups.Select(g => g.Name));
        Assert.Equal(["Солярис", "Дюна"], groups[0].Tasks.Select(t => t.Task.Title).Order().Reverse());
        Assert.Equal(["Купить лампу"], groups[1].Tasks.Select(t => t.Task.Title));
        Assert.Equal("📚", groups[0].Emoji);
        Assert.Equal([new DoneYearDto(Today.Year, 2)], groups[0].Years);
        Assert.All(groups.SelectMany(g => g.Tasks), t => Assert.Equal(Today, t.Date));
    }

    [Fact]
    public async Task D6_K12_UncheckDoneInFeed_BackToItsPlace_UncheckDoneInDay_StaysInDay()
    {
        var ideas = await CreateCategoryAsync();
        var a = await ThoughtAsync("А", ideas.Id);
        var b = await ThoughtAsync("Б", ideas.Id);
        await ThoughtAsync("В", ideas.Id);
        await SetDoneAsync(b.Id);
        Assert.Equal(["А", "В"], (await GetBoardAsync()).SomedayTasks.Select(t => t.Title));

        var back = await SetDoneAsync(b.Id, false);
        Assert.Equal(TaskStateDto.Someday, back.State);
        Assert.Equal(["А", "Б", "В"], (await GetBoardAsync()).SomedayTasks.OrderBy(t => t.SortOrder).Select(t => t.Title));

        await MoveTaskAsync(a.Id, new DayTargetDto(Monday, 0));
        await SetDoneAsync(a.Id);
        Assert.Single((await DoneAsync()).Single().Tasks);
        var open = await SetDoneAsync(a.Id, false);
        Assert.Equal((TaskStateDto.Planned, Monday), (open.State, open.PlannedDate!.Value));
        Assert.Empty(await DoneAsync());
    }

    [Fact]
    public async Task K13_CategoryDeleted_DoneStay_NoCategoryGroup_UncheckGoesToTopOfUnsorted()
    {
        var books = await CreateCategoryAsync("Книги");
        var read = await ThoughtAsync("Дюна", books.Id);
        await ThoughtAsync("Солярис", books.Id);
        await ThoughtAsync("Мысль", null);
        await SetDoneAsync(read.Id);

        // «Удалить вместе с делами» касается только мыслей ленты
        (await Client.DeleteAsync($"/api/categories/{books.Id}?keepTasks=false")).EnsureSuccessStatusCode();
        var group = Assert.Single(await DoneAsync());
        Assert.Equal((null, "без категории", "Дюна"), (group.CategoryId, group.Name, group.Tasks.Single().Task.Title));
        Assert.Equal(TaskStateDto.Done, group.Tasks.Single().Task.State);

        await SetDoneAsync(read.Id, false);
        Assert.Equal(["Дюна", "Мысль"], (await GetBoardAsync()).SomedayTasks.OrderBy(t => t.SortOrder).Select(t => t.Title));
    }

    [Fact]
    public async Task K20_DoneInFeed_Trashed_RestoredStillDone()
    {
        var thought = await ThoughtAsync("Краска", null);
        await SetDoneAsync(thought.Id);
        (await Client.DeleteAsync($"/api/tasks/{thought.Id}")).EnsureSuccessStatusCode();
        Assert.Empty(await DoneAsync());

        var restored = await PostAsync<TaskDto>($"/api/trash/{thought.Id}/restore", new { });
        Assert.Equal((TaskStateDto.Done, (DateOnly?)null), (restored.State, restored.PlannedDate));
        Assert.Single((await DoneAsync()).Single().Tasks);
        Assert.Empty((await GetBoardAsync()).SomedayTasks);
    }

    [Fact]
    public async Task V6_Project_StepsCount_Weeks_StepsNotListedSeparately()
    {
        var repair = await ThoughtAsync("Ремонт", null);
        var a = await PostAsync<StepDto>($"/api/tasks/{repair.Id}/steps", new StepRequest("Обои"));
        var b = await PostAsync<StepDto>($"/api/tasks/{repair.Id}/steps", new StepRequest("Плинтус"));
        await SetDoneAsync(a.Id);
        await SetDoneAsync(b.Id); // последний шаг — родитель отмечается сам (S12)

        var entry = Assert.Single(Assert.Single(await DoneAsync()).Tasks);
        Assert.Equal(("Ремонт", new ProjectSummaryDto(2, 2, 0), 1), (entry.Task.Title, entry.Project, entry.Weeks!.Value));
        Assert.Equal(["Обои", "Плинтус"], entry.Steps.Select(s => s.Title));
    }
}
