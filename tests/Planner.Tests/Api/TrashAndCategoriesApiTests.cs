using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

public class TrashAndCategoriesApiTests : ApiTestBase
{
    [Fact]
    public async Task Trash_Restore_ReturnsToPlan()
    {
        var task = await CreateTaskAsync("Дело", new GridTargetDto(Monday, new TimeOnly(10, 0), 30));

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
        var inbox = await InboxIdAsync();
        var a = await CreateTaskAsync("A", new SomedayTargetDto(inbox, 0));
        var b = await CreateTaskAsync("B", new SomedayTargetDto(inbox, 0));
        var c = await CreateTaskAsync("C", new SomedayTargetDto(inbox, 0));

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
    public async Task DeleteCategory_TasksGoToTrash_RestoreIntoInbox()
    {
        var inbox = await InboxIdAsync();
        var ideas = await CreateCategoryAsync("Идеи");
        var task = await CreateTaskAsync("Идея", new SomedayTargetDto(ideas.Id, 0));

        var response = await Client.DeleteAsync($"/api/categories/{ideas.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var trash = await GetAsync<List<TaskDto>>("/api/trash");
        Assert.Single(trash, t => t.Id == task.Id);

        var restored = await PostAsync<TaskDto>($"/api/trash/{task.Id}/restore", new { });
        Assert.Equal(TaskStateDto.Someday, restored.State);
        Assert.Equal(inbox, restored.CategoryId);
    }

    [Fact]
    public async Task Inbox_CannotBeDeleted_ButCanBeRenamed()
    {
        var inbox = await InboxIdAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Client.DeleteAsync($"/api/categories/{inbox}")).StatusCode);

        var renamed = await PutAsync<CategoryDto>($"/api/categories/{inbox}", new UpdateCategoryRequest("Разное", null));
        Assert.Equal("Разное", renamed.Name);
    }

    [Fact]
    public async Task Category_CollapseAndReorder()
    {
        var inbox = await InboxIdAsync();
        var a = await CreateCategoryAsync("A");
        var b = await CreateCategoryAsync("B");

        var collapsed = await PutAsync<CategoryDto>($"/api/categories/{a.Id}", new UpdateCategoryRequest(null, false));
        Assert.False(collapsed.IsExpanded);

        await Client.PostAsJsonAsync("/api/categories/reorder", new ReorderRequest([b.Id, inbox, a.Id]));

        var order = (await GetBoardAsync()).Categories.Select(c => c.Id);
        Assert.Equal([b.Id, inbox, a.Id], order);
    }

    [Fact]
    public async Task MoveBetweenCategories()
    {
        var inbox = await InboxIdAsync();
        var other = await CreateCategoryAsync("Другое");
        var x = await CreateTaskAsync("X", new SomedayTargetDto(other.Id, 0));
        var y = await CreateTaskAsync("Y", new SomedayTargetDto(other.Id, 1));
        var task = await CreateTaskAsync("Дело", new SomedayTargetDto(inbox, 0));

        await MoveTaskAsync(task.Id, new SomedayTargetDto(other.Id, 1));

        var order = (await GetBoardAsync()).SomedayTasks
            .Where(t => t.CategoryId == other.Id).OrderBy(t => t.SortOrder).Select(t => t.Id);
        Assert.Equal([x.Id, task.Id, y.Id], order);
    }
}
