using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

public class TasksApiTests : ApiTestBase
{
    private async Task<int[]> DayOrderAsync(DateOnly date) =>
        (await GetWeekAsync()).Tasks.Where(t => t.PlannedDate == date).Select(t => t.Id).ToArray();

    [Fact]
    public async Task CreateInCategory_NewTaskGoesToTop()
    {
        var ideas = await CreateCategoryAsync();

        var first = await CreateTaskAsync("Первое", new SomedayTargetDto(ideas.Id, 0));
        var second = await CreateTaskAsync("Второе", new SomedayTargetDto(ideas.Id, 0));

        var board = await GetBoardAsync();
        Assert.Equal([second.Id, first.Id], board.SomedayTasks.OrderBy(t => t.SortOrder).Select(t => t.Id));
    }

    [Fact]
    public async Task CreateInCategory_HugeIndex_GoesToEnd()
    {
        var ideas = await CreateCategoryAsync();
        var first = await CreateTaskAsync("Первое", new SomedayTargetDto(ideas.Id, 0));
        var last = await CreateTaskAsync("В конец", new SomedayTargetDto(ideas.Id, int.MaxValue));

        var board = await GetBoardAsync();
        Assert.Equal([first.Id, last.Id], board.SomedayTasks.OrderBy(t => t.SortOrder).Select(t => t.Id));
    }

    [Fact]
    public async Task Move_SomedayToDay_AndBack_ReturnsToOwnCategory()
    {
        var books = await CreateCategoryAsync("Книги");
        var task = await CreateTaskAsync("Прочитать Кови", new SomedayTargetDto(books.Id, 0));

        var planned = await MoveTaskAsync(task.Id, new DayTargetDto(Monday.AddDays(2), 0));

        Assert.Equal(TaskStateDto.Planned, planned.State);
        Assert.Equal(books.Id, planned.CategoryId);
        Assert.Contains((await GetWeekAsync()).Tasks, t => t.Id == task.Id);
        Assert.DoesNotContain((await GetBoardAsync()).SomedayTasks, t => t.Id == task.Id);

        var back = await MoveTaskAsync(task.Id, new SomedayTargetDto(books.Id, 0));

        Assert.Equal(TaskStateDto.Someday, back.State);
        Assert.Null(back.PlannedDate);
        Assert.Empty((await GetWeekAsync()).Tasks);
    }

    [Fact]
    public async Task Day_UntimedOrderByIndex()
    {
        var a = await CreateInDayAsync("A", index: 0);
        var b = await CreateInDayAsync("B", index: 1);
        var c = await CreateInDayAsync("C", index: 1); // между A и B

        Assert.Equal([a.Id, c.Id, b.Id], await DayOrderAsync(Monday));
    }

    [Fact]
    public async Task Day_TimedFirstByTime_ThenUntimed()
    {
        var untimed = await CreateInDayAsync("Без времени");
        var late = await UpdateTaskAsync(await CreateInDayAsync("Стоматолог"), new TimeOnly(15, 0));
        var early = await UpdateTaskAsync(await CreateInDayAsync("Стендап"), new TimeOnly(10, 0));

        Assert.Equal([early.Id, late.Id, untimed.Id], await DayOrderAsync(Monday));
    }

    [Fact]
    public async Task RemovingTime_PutsTaskAtEndOfUntimed()
    {
        var a = await CreateInDayAsync("A", index: 0);
        var timed = await UpdateTaskAsync(await CreateInDayAsync("Встреча", index: 0), new TimeOnly(9, 0));
        var b = await CreateInDayAsync("B", index: 1);

        await UpdateTaskAsync(timed, startTime: null);

        Assert.Equal([a.Id, b.Id, timed.Id], await DayOrderAsync(Monday));
    }

    [Fact]
    public async Task MoveTimedTask_ToAnotherDay_KeepsTime()
    {
        var task = await UpdateTaskAsync(await CreateInDayAsync("Встреча"), new TimeOnly(15, 30));

        var moved = await MoveTaskAsync(task.Id, new DayTargetDto(Monday.AddDays(1), 0));

        Assert.Equal(new TimeOnly(15, 30), moved.StartTime);
        Assert.Equal(Monday.AddDays(1), moved.PlannedDate);
    }

    [Fact]
    public async Task SetTime_OnSomedayTask_Returns400()
    {
        var ideas = await CreateCategoryAsync();
        var task = await CreateTaskAsync("Мысль", new SomedayTargetDto(ideas.Id, 0));

        var response = await Client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Мысль", null, null, null, new TimeOnly(9, 0), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatedInPlan_HasNoCategory_ReturnsWhereDropped()
    {
        var ideas = await CreateCategoryAsync();
        var task = await CreateInDayAsync("Позвонить");
        Assert.Null(task.CategoryId);

        var back = await MoveTaskAsync(task.Id, new SomedayTargetDto(ideas.Id, 0));

        Assert.Equal(ideas.Id, back.CategoryId);
    }

    [Fact]
    public async Task Update_SeveralRoles_ChangeAndClear_RoleDeletionRemovesIt()
    {
        var roles = (await GetBoardAsync()).Roles.Where(r => !r.IsSharpenSaw).ToList();
        var task = await CreateInDayAsync("Семейный ужин с коллегами");

        var both = await UpdateTaskAsync(task, roleIds: [roles[2].Id, roles[1].Id, roles[1].Id]);
        Assert.Equal(new[] { roles[1].Id, roles[2].Id }.Order(), both.RoleIds); // без повторов, по Id

        var one = await UpdateTaskAsync(both, roleIds: [roles[2].Id]);
        Assert.Equal([roles[2].Id], one.RoleIds);

        await UpdateTaskAsync(one, roleIds: [roles[0].Id, roles[2].Id]);
        await Client.DeleteAsync($"/api/roles/{roles[2].Id}");
        Assert.Equal([roles[0].Id], (await GetWeekAsync()).Tasks.Single(t => t.Id == task.Id).RoleIds);

        var none = await PutAsync<TaskDto>($"/api/tasks/{task.Id}", new UpdateTaskRequest(task.Title, null, null, null, null, null));
        Assert.Empty(none.RoleIds);
    }

    [Fact]
    public async Task Update_UnknownRole_NotFound()
    {
        var task = await CreateInDayAsync("Дело");

        var response = await Client.PutAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Дело", null, [99999], null, null, null));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_TitleNotesRoleQuadrant()
    {
        var board = await GetBoardAsync();
        var role = board.Roles.First(r => !r.IsSharpenSaw);
        var task = await CreateInDayAsync("Дело");

        var updated = await PutAsync<TaskDto>($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Новое название", "заметка", [role.Id], 2, null, null));

        Assert.Equal("Новое название", updated.Title);
        Assert.Equal("заметка", updated.Notes);
        Assert.Equal([role.Id], updated.RoleIds);
        Assert.Equal(2, updated.Quadrant);
    }

    [Fact]
    public async Task Update_InvalidQuadrant_Returns400()
    {
        var task = await CreateInDayAsync("Дело");

        var response = await Client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Дело", null, null, 7, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Done_InPlanAndInSomeday_NotInTrash()
    {
        var ideas = await CreateCategoryAsync();
        var someday = await CreateTaskAsync("Мысль", new SomedayTargetDto(ideas.Id, 0));
        var planned = await CreateInDayAsync("Дело");

        var done = await PutAsync<TaskDto>($"/api/tasks/{planned.Id}/done", new SetDoneRequest(true));
        Assert.Equal(TaskStateDto.Done, done.State);

        // D2: мысль отмечена прямо в ленте — из ленты уходит, категория сохраняется
        var doneInFeed = await PutAsync<TaskDto>($"/api/tasks/{someday.Id}/done", new SetDoneRequest(true));
        Assert.Equal((TaskStateDto.Done, (DateOnly?)null, ideas.Id), (doneInFeed.State, doneInFeed.PlannedDate, doneInFeed.CategoryId));
        Assert.Empty((await GetBoardAsync()).SomedayTasks);

        (await Client.DeleteAsync($"/api/tasks/{planned.Id}")).EnsureSuccessStatusCode();
        var response = await Client.PutAsJsonAsync($"/api/tasks/{planned.Id}/done", new SetDoneRequest(true));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_Returns404()
    {
        var response = await Client.PostAsJsonAsync("/api/tasks/999/move", (TaskTargetDto)new DayTargetDto(Monday, 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EmptyTitle_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/tasks",
            new CreateTaskRequest("   ", new DayTargetDto(Monday, 0)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Target_KindNotFirst_IsAccepted()
    {
        var json = """{"target":{"date":"2026-09-28","index":0,"kind":"day"},"title":"Дело"}""";

        var response = await Client.PostAsync("/api/tasks",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        var task = await ReadAsync<TaskDto>(response);
        Assert.Equal(Monday, task.PlannedDate);
    }
}
