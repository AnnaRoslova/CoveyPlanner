using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

public class TasksApiTests : ApiTestBase
{
    [Fact]
    public async Task CreateInCategory_NewTaskGoesToTop()
    {
        var inbox = await InboxIdAsync();

        var first = await CreateTaskAsync("Первое", new SomedayTargetDto(inbox, 0));
        var second = await CreateTaskAsync("Второе", new SomedayTargetDto(inbox, 0));

        var board = await GetBoardAsync();
        Assert.Equal([second.Id, first.Id], board.SomedayTasks.OrderBy(t => t.SortOrder).Select(t => t.Id));
    }

    [Fact]
    public async Task Move_SomedayToGrid_AndBack_ReturnsToOwnCategory()
    {
        var books = await CreateCategoryAsync("Книги");
        var task = await CreateTaskAsync("Прочитать Кови", new SomedayTargetDto(books.Id, 0));

        var planned = await MoveTaskAsync(task.Id, new GridTargetDto(Monday.AddDays(2), new TimeOnly(9, 40), 50));

        Assert.Equal(TaskStateDto.Planned, planned.State);
        Assert.Equal(new TimeOnly(9, 30), planned.StartTime); // выровнено по шагу 30
        Assert.Equal(60, planned.DurationMinutes);
        Assert.Equal(books.Id, planned.CategoryId);
        Assert.Contains((await GetWeekAsync()).Tasks, t => t.Id == task.Id);
        Assert.DoesNotContain((await GetBoardAsync()).SomedayTasks, t => t.Id == task.Id);

        var back = await MoveTaskAsync(task.Id, new SomedayTargetDto(books.Id, 0));

        Assert.Equal(TaskStateDto.Someday, back.State);
        Assert.Null(back.PlannedDate);
        Assert.Empty((await GetWeekAsync()).Tasks);
    }

    [Fact]
    public async Task DayPriorities_OrderByIndex()
    {
        var a = await CreateTaskAsync("A", new DayPrioritiesTargetDto(Monday, 0));
        var b = await CreateTaskAsync("B", new DayPrioritiesTargetDto(Monday, 1));
        var c = await CreateTaskAsync("C", new DayPrioritiesTargetDto(Monday, 1)); // между A и B

        var week = await GetWeekAsync();
        var order = week.Tasks.Where(t => t.PlannedDate == Monday).OrderBy(t => t.SortOrder).Select(t => t.Id);
        Assert.Equal([a.Id, c.Id, b.Id], order);
    }

    [Fact]
    public async Task CreatedInPlan_ReturnsToInbox()
    {
        var inbox = await InboxIdAsync();
        var task = await CreateTaskAsync("Позвонить", new DayPrioritiesTargetDto(Monday, 0));
        Assert.Null(task.CategoryId);

        var restored = await MoveTaskAsync(task.Id, new SomedayTargetDto(inbox, 0));

        Assert.Equal(inbox, restored.CategoryId);
    }

    [Fact]
    public async Task Update_TitleNotesRoleQuadrant()
    {
        var board = await GetBoardAsync();
        var role = board.Roles.First(r => !r.IsSharpenSaw);
        var task = await CreateTaskAsync("Дело", new DayPrioritiesTargetDto(Monday, 0));

        var updated = await PutAsync<TaskDto>($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Новое название", "заметка", role.Id, 2));

        Assert.Equal("Новое название", updated.Title);
        Assert.Equal("заметка", updated.Notes);
        Assert.Equal(role.Id, updated.RoleId);
        Assert.Equal(2, updated.Quadrant);
    }

    [Fact]
    public async Task Update_InvalidQuadrant_Returns400()
    {
        var task = await CreateTaskAsync("Дело", new DayPrioritiesTargetDto(Monday, 0));

        var response = await Client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Дело", null, null, 7));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Done_OnlyForPlanned()
    {
        var inbox = await InboxIdAsync();
        var someday = await CreateTaskAsync("Мысль", new SomedayTargetDto(inbox, 0));
        var planned = await CreateTaskAsync("Дело", new DayPrioritiesTargetDto(Monday, 0));

        var done = await PutAsync<TaskDto>($"/api/tasks/{planned.Id}/done", new SetDoneRequest(true));
        Assert.Equal(TaskStateDto.Done, done.State);

        var response = await Client.PutAsJsonAsync($"/api/tasks/{someday.Id}/done", new SetDoneRequest(true));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_Returns404()
    {
        var response = await Client.PostAsJsonAsync("/api/tasks/999/move", (TaskTargetDto)new DayPrioritiesTargetDto(Monday, 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Target_KindNotFirst_IsAccepted()
    {
        var json = """{"target":{"date":"2026-09-28","index":0,"kind":"priorities"},"title":"Дело"}""";

        var response = await Client.PostAsync("/api/tasks",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        var task = await ReadAsync<TaskDto>(response);
        Assert.Equal(Monday, task.PlannedDate);
    }

    [Fact]
    public async Task EmptyTitle_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/tasks",
            new CreateTaskRequest("   ", new DayPrioritiesTargetDto(Monday, 0)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
