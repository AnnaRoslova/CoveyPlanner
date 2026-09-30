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

    private Task<GoalResultDto> ScheduleAsync(int goalId, DateOnly date) =>
        PostTargetAsync<GoalResultDto>($"/api/goals/{goalId}/schedule", new DayTargetDto(date, int.MaxValue));

    private Task<GoalResultDto> ToggleAsync(int goalId, DateOnly today) =>
        PostAsync<GoalResultDto>($"/api/goals/{goalId}/toggle", new ToggleGoalRequest(today));

    private Task<WeeklyGoalDto> SetTimesAsync(int goalId, int times) =>
        PutAsync<WeeklyGoalDto>($"/api/goals/{goalId}", new UpdateGoalRequest(null, times));

    private Task<TaskDto> SetDoneAsync(int taskId) =>
        PutAsync<TaskDto>($"/api/tasks/{taskId}/done", new SetDoneRequest(true));

    private async Task<WeeklyGoalDto> GoalAsync(int goalId) => (await GetWeekAsync()).Goals.Single(g => g.Id == goalId);

    [Fact]
    public async Task Goal_ScheduleToDay_AddsNewSessionEachTime()
    {
        var role = await FirstUserRoleAsync();
        var goal = await CreateGoalAsync(role.Id, "Спортзал");
        Assert.Equal(1, goal.TimesPerWeek);

        var thursday = await ScheduleAsync(goal.Id, Monday.AddDays(3));
        var friday = await ScheduleAsync(goal.Id, Monday.AddDays(4));

        Assert.NotEqual(thursday.Task!.Id, friday.Task!.Id);
        Assert.Equal(goal.Id, friday.Task.GoalId);
        Assert.Equal(role.Id, friday.Task.RoleId);
        Assert.Null(friday.Task.CategoryId);
        Assert.Equal([Monday.AddDays(3), Monday.AddDays(4)], friday.Goal.Sessions.Select(s => s.PlannedDate));
        Assert.Equal(2, (await GetWeekAsync()).Tasks.Count(t => t.GoalId == goal.Id));
    }

    [Fact]
    public async Task Goal_TimesPerWeek_DoneWhenEnoughSessionsChecked()
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Спортзал");
        Assert.Equal(2, (await SetTimesAsync(goal.Id, 2)).TimesPerWeek);
        var tuesday = (await ScheduleAsync(goal.Id, Monday.AddDays(1))).Task!;
        var thursday = (await ScheduleAsync(goal.Id, Monday.AddDays(3))).Task!;

        await SetDoneAsync(tuesday.Id);
        var afterOne = await GoalAsync(goal.Id);
        Assert.Equal(1, afterOne.DoneCount);
        Assert.False(afterOne.IsDone);

        await SetDoneAsync(thursday.Id);
        Assert.True((await GoalAsync(goal.Id)).IsDone);

        // Стало нужно 3 раза — снова не выполнена
        await SetTimesAsync(goal.Id, 3);
        Assert.False((await GoalAsync(goal.Id)).IsDone);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task Goal_TimesPerWeek_OutOfRange_Returns400(int times)
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Цель");

        var response = await Client.PutAsJsonAsync($"/api/goals/{goal.Id}", new UpdateGoalRequest(null, times));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Goal_Toggle_ChecksNearestSession_ThenAddsTodaySession_ThenUnchecksLast()
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Бег");
        await SetTimesAsync(goal.Id, 3);
        var wednesday = Monday.AddDays(2);
        var friday = (await ScheduleAsync(goal.Id, Monday.AddDays(4))).Task!;
        var tuesday = (await ScheduleAsync(goal.Id, Monday.AddDays(1))).Task!;

        // Сегодня занятия нет — отмечается самое раннее (вторник), потом следующее (пятница)
        Assert.Equal(tuesday.Id, (await ToggleAsync(goal.Id, wednesday)).Task!.Id);
        Assert.Equal(friday.Id, (await ToggleAsync(goal.Id, wednesday)).Task!.Id);

        // Невыполненных не осталось — выполненное занятие на сегодня
        var added = await ToggleAsync(goal.Id, wednesday);
        Assert.Equal(wednesday, added.Task!.PlannedDate);
        Assert.Equal(TaskStateDto.Done, added.Task.State);
        Assert.True(added.Goal.IsDone);
        Assert.Contains((await GetWeekAsync()).Tasks, t => t.Id == added.Task.Id);

        // Цель выполнена — снимается отметка с последнего отмеченного
        var undone = await ToggleAsync(goal.Id, wednesday);
        Assert.Equal(added.Task.Id, undone.Task!.Id);
        Assert.Equal(TaskStateDto.Planned, undone.Task.State);
        Assert.Equal(2, undone.Goal.DoneCount);
    }

    [Fact]
    public async Task Goal_Toggle_PrefersTodaysSession()
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Бег");
        await ScheduleAsync(goal.Id, Monday.AddDays(1));
        var today = (await ScheduleAsync(goal.Id, Monday.AddDays(3))).Task!;

        var result = await ToggleAsync(goal.Id, Monday.AddDays(3));

        Assert.Equal(today.Id, result.Task!.Id);
    }

    [Fact]
    public async Task Goal_Toggle_OtherWeek_AddsSessionWithoutDay()
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Бег");

        var result = await ToggleAsync(goal.Id, Monday.AddDays(9));

        Assert.Null(result.Task!.PlannedDate);
        Assert.True(result.Goal.IsDone);
        Assert.Empty((await GetWeekAsync()).Tasks);
    }

    [Fact]
    public async Task Goal_Rename_RenamesSessionsWithSameTitleOnly()
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Пробежка");
        var same = (await ScheduleAsync(goal.Id, Monday)).Task!;
        var own = (await ScheduleAsync(goal.Id, Monday.AddDays(1))).Task!;
        await PutAsync<TaskDto>($"/api/tasks/{own.Id}", new UpdateTaskRequest("Пробежка 10 км", null, own.RoleId, null, null));
        Assert.Equal("Пробежка", (await GoalAsync(goal.Id)).Title); // переименование занятия цель не меняет

        await PutAsync<WeeklyGoalDto>($"/api/goals/{goal.Id}", new UpdateGoalRequest("Бег", null));

        var tasks = (await GetWeekAsync()).Tasks.ToDictionary(t => t.Id);
        Assert.Equal("Бег", tasks[same.Id].Title);
        Assert.Equal("Пробежка 10 км", tasks[own.Id].Title);
    }

    [Fact]
    public async Task Goal_FromSomedayTask_FirstScheduleUsesThatTask()
    {
        var role = await FirstUserRoleAsync();
        var home = await CreateCategoryAsync("Дом");
        var task = await CreateTaskAsync("Разобрать шкаф", new SomedayTargetDto(home.Id, 0));

        var result = await PostAsync<GoalResultDto>("/api/goals/from-task", new GoalFromTaskRequest(task.Id, role.Id, Monday, 0));

        Assert.Equal("Разобрать шкаф", result.Goal.Title);
        var session = Assert.Single(result.Goal.Sessions);
        Assert.Equal(task.Id, session.TaskId);
        Assert.Null(session.PlannedDate);
        Assert.Empty((await GetBoardAsync()).SomedayTasks);

        // Второй раз то же дело — нельзя
        var again = await Client.PostAsJsonAsync("/api/goals/from-task", new GoalFromTaskRequest(task.Id, role.Id, Monday, 0));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        Assert.Equal(task.Id, (await ScheduleAsync(result.Goal.Id, Monday)).Task!.Id);
        Assert.NotEqual(task.Id, (await ScheduleAsync(result.Goal.Id, Monday.AddDays(1))).Task!.Id);
    }

    [Fact]
    public async Task DeleteGoal_UnscheduledSession_BackToItsCategory()
    {
        var role = await FirstUserRoleAsync();
        var home = await CreateCategoryAsync("Дом");
        var task = await CreateTaskAsync("Разобрать шкаф", new SomedayTargetDto(home.Id, 0));
        var result = await PostAsync<GoalResultDto>("/api/goals/from-task", new GoalFromTaskRequest(task.Id, role.Id, Monday, 0));

        await Client.DeleteAsync($"/api/goals/{result.Goal.Id}");

        var back = Assert.Single((await GetBoardAsync()).SomedayTasks);
        Assert.Equal(task.Id, back.Id);
        Assert.Equal(home.Id, back.CategoryId);
        Assert.Null(back.GoalId);
    }

    /// <summary>Цель из дела ленты (дело — занятие без дня), потом удалили категорию этого дела.</summary>
    private async Task<(GoalResultDto Goal, CategoryDto Other)> WeekOnlyTaskWithoutCategoryAsync(int roleId)
    {
        var doomed = await CreateCategoryAsync("Удаляемая");
        var other = await CreateCategoryAsync("Другая");
        var task = await CreateTaskAsync("Дело", new SomedayTargetDto(doomed.Id, 0));
        var goal = await PostAsync<GoalResultDto>("/api/goals/from-task", new GoalFromTaskRequest(task.Id, roleId, Monday, 0));
        await Client.DeleteAsync($"/api/categories/{doomed.Id}");
        return (goal, other);
    }

    [Fact]
    public async Task DeleteGoal_DoneSessionsStay_OpenSessionsGoToTrash()
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Спортзал");
        await SetTimesAsync(goal.Id, 3);
        var done = (await ScheduleAsync(goal.Id, Monday)).Task!;
        var open = (await ScheduleAsync(goal.Id, Monday.AddDays(1))).Task!;
        await SetDoneAsync(done.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/goals/{goal.Id}")).StatusCode);

        var week = await GetWeekAsync();
        Assert.Empty(week.Goals);
        var kept = Assert.Single(week.Tasks);
        Assert.Equal(done.Id, kept.Id);
        Assert.Null(kept.GoalId);
        Assert.Contains(await GetAsync<List<TaskDto>>("/api/trash"), t => t.Id == open.Id);
    }

    [Fact]
    public async Task Goal_ToSomeday_OpenSessionsGoToTrash()
    {
        var ideas = await CreateCategoryAsync();
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Бег");
        var open = (await ScheduleAsync(goal.Id, Monday)).Task!;

        var task = await PostAsync<TaskDto>($"/api/goals/{goal.Id}/to-someday", new GoalToSomedayRequest(ideas.Id, 0));

        Assert.NotEqual(open.Id, task.Id);
        Assert.Equal(TaskStateDto.Someday, task.State);
        Assert.Empty((await GetWeekAsync()).Tasks);
        Assert.Contains(await GetAsync<List<TaskDto>>("/api/trash"), t => t.Id == open.Id);
    }

    [Fact]
    public async Task DeleteGoal_TaskWithoutCategory_AsksCategory()
    {
        var role = await FirstUserRoleAsync();
        var (goal, other) = await WeekOnlyTaskWithoutCategoryAsync(role.Id);

        await AssertCategoryRequiredAsync(await Client.DeleteAsync($"/api/goals/{goal.Goal.Id}"));
        Assert.Single((await GetWeekAsync()).Goals); // ничего не удалилось

        var ok = await Client.DeleteAsync($"/api/goals/{goal.Goal.Id}?categoryId={other.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        var back = Assert.Single((await GetBoardAsync()).SomedayTasks);
        Assert.Equal(other.Id, back.CategoryId);
    }

    [Fact]
    public async Task DeleteRole_TaskWithoutCategory_AsksCategory()
    {
        var role = await PostAsync<RoleDto>("/api/roles", new CreateRoleRequest("Временная", null));
        var (_, other) = await WeekOnlyTaskWithoutCategoryAsync(role.Id);

        await AssertCategoryRequiredAsync(await Client.DeleteAsync($"/api/roles/{role.Id}"));
        Assert.Contains((await GetBoardAsync()).Roles, r => r.Id == role.Id);

        await Client.DeleteAsync($"/api/roles/{role.Id}?categoryId={other.Id}");
        Assert.DoesNotContain((await GetBoardAsync()).Roles, r => r.Id == role.Id);
        Assert.Equal(other.Id, Assert.Single((await GetBoardAsync()).SomedayTasks).CategoryId);
    }

    [Fact]
    public async Task Goal_ToSomeday_CreatesTask_AndRemovesGoal()
    {
        var role = await FirstUserRoleAsync();
        var ideas = await CreateCategoryAsync();
        var goal = await CreateGoalAsync(role.Id, "Выучить песню");

        var task = await PostAsync<TaskDto>($"/api/goals/{goal.Id}/to-someday", new GoalToSomedayRequest(ideas.Id, 0));

        Assert.Equal(TaskStateDto.Someday, task.State);
        Assert.Equal("Выучить песню", task.Title);
        Assert.Empty((await GetWeekAsync()).Goals);
    }

    [Fact]
    public async Task Goal_MoveToOtherRole_SessionsFollow()
    {
        var roles = (await GetBoardAsync()).Roles.Where(r => !r.IsSharpenSaw).ToList();
        var goal = await CreateGoalAsync(roles[0].Id, "Цель");
        var session = (await ScheduleAsync(goal.Id, Monday)).Task!;

        var moved = await PostAsync<WeeklyGoalDto>($"/api/goals/{goal.Id}/move", new MoveGoalRequest(roles[1].Id, 0));

        Assert.Equal(roles[1].Id, moved.RoleId);
        Assert.Equal(roles[1].Id, (await GetWeekAsync()).Tasks.Single(t => t.Id == session.Id).RoleId);
    }

    [Fact]
    public async Task TaskMovedToFeed_LeavesGoalSessions()
    {
        var ideas = await CreateCategoryAsync();
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Цель");
        var scheduled = await ScheduleAsync(goal.Id, Monday);

        var moved = await MoveTaskAsync(scheduled.Task!.Id, new SomedayTargetDto(ideas.Id, 0));

        Assert.Null(moved.GoalId);
        var week = await GetWeekAsync();
        Assert.Empty(week.Goals.Single().Sessions);
        Assert.Empty(week.Tasks);
    }

    [Fact]
    public async Task TrashedSession_NotCounted_RestoredCountsAgain()
    {
        var goal = await CreateGoalAsync((await FirstUserRoleAsync()).Id, "Цель");
        var session = (await ScheduleAsync(goal.Id, Monday)).Task!;
        await SetDoneAsync(session.Id);
        Assert.True((await GoalAsync(goal.Id)).IsDone);

        await Client.DeleteAsync($"/api/tasks/{session.Id}");
        Assert.Empty((await GoalAsync(goal.Id)).Sessions);

        await Client.PostAsync($"/api/trash/{session.Id}/restore", null);
        Assert.True((await GoalAsync(goal.Id)).IsDone);
    }

    [Fact]
    public async Task DeleteRole_ReturnsWeekOnlyTasksToFeed()
    {
        var role = await PostAsync<RoleDto>("/api/roles", new CreateRoleRequest("Временная", null));
        var ideas = await CreateCategoryAsync();
        var task = await CreateTaskAsync("Дело", new SomedayTargetDto(ideas.Id, 0));
        await PostAsync<GoalResultDto>("/api/goals/from-task", new GoalFromTaskRequest(task.Id, role.Id, Monday, 0));

        await Client.DeleteAsync($"/api/roles/{role.Id}");

        var back = Assert.Single((await GetBoardAsync()).SomedayTasks);
        Assert.Null(back.RoleId);
        Assert.Empty((await GetWeekAsync()).Goals);
    }
}
