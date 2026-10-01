using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

public class TrashAndCategoriesApiTests : ApiTestBase
{
    [Fact]
    public async Task Trash_Restore_ReturnsToDay_WithTime()
    {
        var task = await UpdateTaskAsync(await CreateInDayAsync("Дело"), new TimeOnly(10, 0));

        await Client.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Empty((await GetWeekAsync()).Tasks);
        Assert.Equal(1, (await GetBoardAsync()).TrashCount);

        var restored = await PostAsync<TaskDto>($"/api/trash/{task.Id}/restore", new { });

        Assert.Equal(TaskStateDto.Planned, restored.State);
        Assert.Equal(new TimeOnly(10, 0), restored.StartTime);
        Assert.Equal(0, (await GetBoardAsync()).TrashCount);
    }

    [Fact]
    public async Task Trash_DeleteForeverAndEmpty()
    {
        var ideas = await CreateCategoryAsync();
        var a = await CreateTaskAsync("A", new SomedayTargetDto(ideas.Id, 0));
        var b = await CreateTaskAsync("B", new SomedayTargetDto(ideas.Id, 0));
        var c = await CreateTaskAsync("C", new SomedayTargetDto(ideas.Id, 0));

        // Навсегда можно удалить только из корзины
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.DeleteAsync($"/api/trash/{a.Id}")).StatusCode);

        foreach (var id in new[] { a.Id, b.Id, c.Id })
        {
            await Client.DeleteAsync($"/api/tasks/{id}");
        }

        await Client.DeleteAsync($"/api/trash/{a.Id}");
        Assert.Equal(2, (await GetAsync<List<TaskDto>>("/api/trash")).Count);

        await Client.DeleteAsync("/api/trash");
        Assert.Empty(await GetAsync<List<TaskDto>>("/api/trash"));
    }

    [Fact]
    public async Task DeleteCategory_WithTasks_TasksGoToTrash_RestoreToUnsorted()
    {
        var ideas = await CreateCategoryAsync("Идеи");
        var task = await CreateTaskAsync("Идея", new SomedayTargetDto(ideas.Id, 0));

        var response = await Client.DeleteAsync($"/api/categories/{ideas.Id}?keepTasks=false");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await GetAsync<List<TaskDto>>("/api/trash"), t => t.Id == task.Id);

        // Своей категории больше нет — дело возвращается в «неразобранное»
        var restored = await PostAsync<TaskDto>($"/api/trash/{task.Id}/restore", new { });
        Assert.Equal(TaskStateDto.Someday, restored.State);
        Assert.Null(restored.CategoryId);
    }

    [Fact]
    public async Task DeleteCategory_KeepTasks_MovesThemToTopOfUnsorted_InOrder()
    {
        var ideas = await CreateCategoryAsync("Идеи");
        await CreateTaskAsync("Старая мысль", new SomedayTargetDto(null, 0));
        await CreateTaskAsync("Б", new SomedayTargetDto(ideas.Id, 0));
        await CreateTaskAsync("А", new SomedayTargetDto(ideas.Id, 0));

        var response = await Client.DeleteAsync($"/api/categories/{ideas.Id}?keepTasks=true");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var board = await GetBoardAsync();
        Assert.Empty(board.Categories);
        Assert.Equal(0, board.TrashCount);
        Assert.Equal(["А", "Б", "Старая мысль"],
            board.SomedayTasks.Where(t => t.CategoryId is null).OrderBy(t => t.SortOrder).Select(t => t.Title));
    }

    [Fact]
    public async Task Restore_ReturnsToOwnCategory()
    {
        var ideas = await CreateCategoryAsync("Идеи");
        var task = await CreateTaskAsync("Идея", new SomedayTargetDto(ideas.Id, 0));
        await Client.DeleteAsync($"/api/tasks/{task.Id}");

        var restored = await PostAsync<TaskDto>($"/api/trash/{task.Id}/restore", new { });

        Assert.Equal(ideas.Id, restored.CategoryId);
    }

    [Fact]
    public async Task Category_EmojiCreateChangeClear()
    {
        var created = await CreateCategoryAsync("Книги", "📚");
        Assert.Equal("📚", created.Emoji);

        var renamedOnly = await PutAsync<CategoryDto>($"/api/categories/{created.Id}", new UpdateCategoryRequest("Книги и фильмы", null, null));
        Assert.Equal("📚", renamedOnly.Emoji); // null — не менять

        var changed = await PutAsync<CategoryDto>($"/api/categories/{created.Id}", new UpdateCategoryRequest(null, "🎬", null));
        Assert.Equal("🎬", changed.Emoji);

        var cleared = await PutAsync<CategoryDto>($"/api/categories/{created.Id}", new UpdateCategoryRequest(null, "", null));
        Assert.Null(cleared.Emoji);
    }

    [Fact]
    public async Task Category_CreatedAtTop_GoesFirst()
    {
        await CreateCategoryAsync("Идеи");
        await CreateCategoryAsync("Дом");
        await PostAsync<CategoryDto>("/api/categories", new CreateCategoryRequest("Срочное", null, AtTop: true));

        var names = (await GetBoardAsync()).Categories.OrderBy(c => c.SortOrder).Select(c => c.Name);

        Assert.Equal(["Срочное", "Идеи", "Дом"], names);
    }

    [Fact]
    public async Task Categories_ExpandAndCollapseAll()
    {
        await CreateCategoryAsync("Идеи");
        await CreateCategoryAsync("Дом");

        var collapse = await Client.PutAsJsonAsync("/api/categories/expanded", new SetAllExpandedRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, collapse.StatusCode);
        Assert.All((await GetBoardAsync()).Categories, c => Assert.False(c.IsExpanded));

        await Client.PutAsJsonAsync("/api/categories/expanded", new SetAllExpandedRequest(true));
        Assert.All((await GetBoardAsync()).Categories, c => Assert.True(c.IsExpanded));
    }

    [Fact]
    public async Task AnyCategory_CanBeDeleted()
    {
        var only = await CreateCategoryAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/categories/{only.Id}")).StatusCode);
        Assert.Empty((await GetBoardAsync()).Categories);
    }

    [Fact]
    public async Task Category_CollapseAndReorder()
    {
        var a = await CreateCategoryAsync("A");
        var b = await CreateCategoryAsync("B");
        var c = await CreateCategoryAsync("C");

        var collapsed = await PutAsync<CategoryDto>($"/api/categories/{a.Id}", new UpdateCategoryRequest(null, null, false));
        Assert.False(collapsed.IsExpanded);

        await Client.PostAsJsonAsync("/api/categories/reorder", new ReorderRequest([b.Id, c.Id, a.Id]));

        var order = (await GetBoardAsync()).Categories.Select(x => x.Id);
        Assert.Equal([b.Id, c.Id, a.Id], order);
    }

    [Fact]
    public async Task MoveWithinCategory_IndexCountsWithoutMovedTask()
    {
        var ideas = await CreateCategoryAsync();
        var a = await CreateTaskAsync("A", new SomedayTargetDto(ideas.Id, 0));
        var b = await CreateTaskAsync("B", new SomedayTargetDto(ideas.Id, 1));
        var c = await CreateTaskAsync("C", new SomedayTargetDto(ideas.Id, 2));

        // Перетаскивание A в самый низ: среди [B, C] (без A) это позиция 2
        await MoveTaskAsync(a.Id, new SomedayTargetDto(ideas.Id, 2));

        var order = (await GetBoardAsync()).SomedayTasks.OrderBy(t => t.SortOrder).Select(t => t.Id);
        Assert.Equal([b.Id, c.Id, a.Id], order);
    }

    [Fact]
    public async Task MoveBetweenCategories()
    {
        var ideas = await CreateCategoryAsync("Идеи");
        var other = await CreateCategoryAsync("Другое");
        var x = await CreateTaskAsync("X", new SomedayTargetDto(other.Id, 0));
        var y = await CreateTaskAsync("Y", new SomedayTargetDto(other.Id, 1));
        var task = await CreateTaskAsync("Дело", new SomedayTargetDto(ideas.Id, 0));

        await MoveTaskAsync(task.Id, new SomedayTargetDto(other.Id, 1));

        var order = (await GetBoardAsync()).SomedayTasks
            .Where(t => t.CategoryId == other.Id).OrderBy(t => t.SortOrder).Select(t => t.Id);
        Assert.Equal([x.Id, task.Id, y.Id], order);
    }
}
