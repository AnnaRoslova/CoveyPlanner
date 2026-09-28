using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

public class RolesAndGoalsApiTests : ApiTestBase
{
    private async Task<RoleDto> FirstUserRoleAsync() =>
        (await GetBoardAsync()).Roles.First(r => !r.IsSharpenSaw);

    private Task<WeeklyGoalDto> CreateGoalAsync(int roleId, string title) =>
        PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, Monday, title));

    [Fact]
    public async Task Role_CreateUpdateReorderDelete()
    {
        var board = await GetBoardAsync();
        var created = await PostAsync<RoleDto>("/api/roles", new CreateRoleRequest("Друзья", null));
        Assert.Contains(created.Color, board.RolePalette);

        var updated = await PutAsync<RoleDto>($"/api/roles/{created.Id}", new UpdateRoleRequest("Друзья и соседи", board.RolePalette[5]));
        Assert.Equal("Друзья и соседи", updated.Name);
        Assert.Equal(board.RolePalette[5], updated.Color);

        var badColor = await Client.PutAsJsonAsync($"/api/roles/{created.Id}", new UpdateRoleRequest(null, "#123456"));
        Assert.Equal(HttpStatusCode.BadRequest, badColor.StatusCode);

        var userIds = (await GetBoardAsync()).Roles.Where(r => !r.IsSharpenSaw).Select(r => r.Id).Reverse().ToList();
        await Client.PostAsJsonAsync("/api/roles/reorder", new ReorderRequest(userIds));
        Assert.Equal(userIds, (await GetBoardAsync()).Roles.Where(r => !r.IsSharpenSaw).Select(r => r.Id));

        await Client.DeleteAsync($"/api/roles/{created.Id}");
        Assert.DoesNotContain((await GetBoardAsync()).Roles, r => r.Id == created.Id);
    }

    [Fact]
    public async Task SawDimension_CannotBeDeletedOrRenamed_ButColorChanges()
    {
        var board = await GetBoardAsync();
        var saw = board.Roles.First(r => r.IsSharpenSaw);

        Assert.Equal(HttpStatusCode.BadRequest, (await Client.DeleteAsync($"/api/roles/{saw.Id}")).StatusCode);
        var rename = await Client.PutAsJsonAsync($"/api/roles/{saw.Id}", new UpdateRoleRequest("Другое", null));
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);

        var recolored = await PutAsync<RoleDto>($"/api/roles/{saw.Id}", new UpdateRoleRequest(saw.Name, board.RolePalette[9]));
        Assert.Equal(board.RolePalette[9], recolored.Color);
    }

    [Fact]
    public async Task Goal_MoreThanThree_IsAllowed()
    {
        var role = await FirstUserRoleAsync();
        for (var i = 1; i <= 5; i++)
        {
            await CreateGoalAsync(role.Id, $"Цель {i}");
        }

        var goals = (await GetWeekAsync()).Goals.Where(g => g.RoleId == role.Id).OrderBy(g => g.SortOrder);
        Assert.Equal(["Цель 1", "Цель 2", "Цель 3", "Цель 4", "Цель 5"], goals.Select(g => g.Title));
    }

    [Fact]
    public async Task Goal_NotMonday_Returns400()
    {
        var role = await FirstUserRoleAsync();

        var response = await Client.PostAsJsonAsync("/api/goals", new CreateGoalRequest(role.Id, Monday.AddDays(1), "Цель"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Goal_ScheduleToGrid_CreatesTask_AndDoneSyncs()
    {
        var role = await FirstUserRoleAsync();
        var goal = await CreateGoalAsync(role.Id, "Пробежка");

        var result = await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new GridTargetDto(Monday.AddDays(1), new TimeOnly(8, 0), 60));

        Assert.NotNull(result.Task);
        Assert.Equal(result.Task.Id, result.Goal.TaskId);
        Assert.Equal(role.Id, result.Task.RoleId);
        Assert.Equal(new TimeOnly(8, 0), result.Goal.StartTime);

        // Отметка у дела → у цели
        await PutAsync<TaskDto>($"/api/tasks/{result.Task.Id}/done", new SetDoneRequest(true));
        Assert.True((await GetWeekAsync()).Goals.Single().IsDone);

        // Отметка у цели → у дела
        await PutAsync<GoalResultDto>($"/api/goals/{goal.Id}", new UpdateGoalRequest(null, false));
        Assert.Equal(TaskStateDto.Planned, (await GetWeekAsync()).Tasks.Single().State);

        // Переименование цели → дело
        await PutAsync<GoalResultDto>($"/api/goals/{goal.Id}", new UpdateGoalRequest("Пробежка 5 км", null));
        Assert.Equal("Пробежка 5 км", (await GetWeekAsync()).Tasks.Single().Title);
    }

    [Fact]
    public async Task Goal_FromSomedayTask_LeavesFeed_AndDeleteReturnsIt()
    {
        var role = await FirstUserRoleAsync();
        var inbox = await InboxIdAsync();
        var task = await CreateTaskAsync("Разобрать шкаф", new SomedayTargetDto(inbox, 0));

        var result = await PostAsync<GoalResultDto>("/api/goals/from-task", new GoalFromTaskRequest(task.Id, role.Id, Monday, 0));

        Assert.Equal("Разобрать шкаф", result.Goal.Title);
        Assert.Equal(task.Id, result.Goal.TaskId);
        Assert.Empty((await GetBoardAsync()).SomedayTasks);

        // Второй раз то же дело — нельзя
        var again = await Client.PostAsJsonAsync("/api/goals/from-task", new GoalFromTaskRequest(task.Id, role.Id, Monday, 0));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        // Удалили цель — дело без дня вернулось в ленту
        await Client.DeleteAsync($"/api/goals/{result.Goal.Id}");
        var back = Assert.Single((await GetBoardAsync()).SomedayTasks);
        Assert.Equal(task.Id, back.Id);
    }

    [Fact]
    public async Task Goal_ToSomeday_CreatesTask_AndRemovesGoal()
    {
        var role = await FirstUserRoleAsync();
        var inbox = await InboxIdAsync();
        var goal = await CreateGoalAsync(role.Id, "Выучить песню");

        var task = await PostAsync<TaskDto>($"/api/goals/{goal.Id}/to-someday", new GoalToSomedayRequest(inbox, 0));

        Assert.Equal(TaskStateDto.Someday, task.State);
        Assert.Equal("Выучить песню", task.Title);
        Assert.Empty((await GetWeekAsync()).Goals);
    }

    [Fact]
    public async Task Goal_MoveToOtherRole()
    {
        var roles = (await GetBoardAsync()).Roles.Where(r => !r.IsSharpenSaw).ToList();
        var goal = await CreateGoalAsync(roles[0].Id, "Цель");

        var moved = await PostAsync<WeeklyGoalDto>($"/api/goals/{goal.Id}/move", new MoveGoalRequest(roles[1].Id, 0));

        Assert.Equal(roles[1].Id, moved.RoleId);
    }

    [Fact]
    public async Task TaskMovedToFeed_UnlinksGoal()
    {
        var role = await FirstUserRoleAsync();
        var inbox = await InboxIdAsync();
        var goal = await CreateGoalAsync(role.Id, "Цель");
        var scheduled = await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayPrioritiesTargetDto(Monday, 0));

        await MoveTaskAsync(scheduled.Task!.Id, new SomedayTargetDto(inbox, 0));

        var week = await GetWeekAsync();
        Assert.Null(week.Goals.Single().TaskId);
        Assert.Empty(week.Tasks);
    }

    [Fact]
    public async Task DeleteRole_ReturnsWeekOnlyTasksToFeed()
    {
        var role = await PostAsync<RoleDto>("/api/roles", new CreateRoleRequest("Временная", null));
        var inbox = await InboxIdAsync();
        var task = await CreateTaskAsync("Дело", new SomedayTargetDto(inbox, 0));
        await PostAsync<GoalResultDto>("/api/goals/from-task", new GoalFromTaskRequest(task.Id, role.Id, Monday, 0));

        await Client.DeleteAsync($"/api/roles/{role.Id}");

        var back = Assert.Single((await GetBoardAsync()).SomedayTasks);
        Assert.Null(back.RoleId);
        Assert.Empty((await GetWeekAsync()).Goals);
    }
}
