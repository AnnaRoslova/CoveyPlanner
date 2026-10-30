using System.Net;
using Planner.Core.Time;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Пропуски: «не нужно» и «не сделано» (docs/specs/skips.md, K, H, S, T2).</summary>
public class SkipsApiTests : ApiTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly DateOnly ThisMonday = Week.StartOf(Today);

    private Task<TaskSkipResultDto> SkipAsync(int id, string kind, string? reason = null) =>
        PutAsync<TaskSkipResultDto>($"/api/tasks/{id}/skip", new SkipTaskRequest(kind, reason));

    private Task<List<TaskSkipDto>> HistoryAsync(int id) => GetAsync<List<TaskSkipDto>>($"/api/tasks/{id}/skips");

    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private async Task<WeeklyGoalDto> GoalAsync(string role, string title, int times, DateOnly week, bool recurring = true)
    {
        var goal = await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync(role), week, title, recurring));
        return (await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, times, null, null, null, null))).Goal;
    }

    private async Task<TaskDto> SessionAsync(WeeklyGoalDto goal, DateOnly day) =>
        (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(day, 0))).Task!;

    private async Task<HabitDto> HabitAsync(int? seriesId) =>
        (await GetAsync<HabitsDto>($"/api/habits?today={Today:yyyy-MM-dd}")).Habits.Single(h => h.Id == seriesId);

    [Fact]
    public async Task NotNeeded_StaysInDay_WithReason_Unskip_BackToOpen()
    {
        var task = await CreateInDayAsync("Стоматолог", ThisMonday);
        var skipped = (await SkipAsync(task.Id, "NotNeeded", "  перенесли приём  ")).Task;

        Assert.Equal(TaskStateDto.Skipped, skipped.State);
        Assert.Equal("NotNeeded", skipped.SkipKind);
        Assert.Equal("перенесли приём", skipped.SkipReason);
        Assert.Equal(ThisMonday, skipped.PlannedDate);
        Assert.Contains((await GetWeekAsync(ThisMonday)).Tasks, t => t.Id == task.Id && t.State == TaskStateDto.Skipped);
        var history = Assert.Single(await HistoryAsync(task.Id));
        Assert.Equal(("NotNeeded", "перенесли приём", ThisMonday), (history.Kind, history.Reason, history.Date));

        var open = await Client.DeleteAsync($"/api/tasks/{task.Id}/skip");
        var reopened = await ReadAsync<TaskDto>(open);
        Assert.Equal(TaskStateDto.Planned, reopened.State);
        Assert.Null(reopened.SkipKind);
        Assert.Empty(await HistoryAsync(task.Id));
    }

    [Fact]
    public async Task SkipAgain_ChangesKindAndReason_OneHistoryRow()
    {
        var task = await CreateInDayAsync("Бег", ThisMonday);
        await SkipAsync(task.Id, "NotNeeded");
        var again = (await SkipAsync(task.Id, "Missed", "заболела")).Task;

        Assert.Equal(("Missed", "заболела"), (again.SkipKind, again.SkipReason));
        Assert.Equal("Missed", Assert.Single(await HistoryAsync(task.Id)).Kind);
    }

    [Fact]
    public async Task CannotSkip_Thought_DoneInFeed_Project_StepWithoutDay_UnknownKind()
    {
        var category = await CreateCategoryAsync();
        var thought = await CreateTaskAsync("Мысль", new SomedayTargetDto(category.Id, 0));
        var done = await CreateTaskAsync("Отмечено в ленте", new SomedayTargetDto(category.Id, 0));
        await SetTaskDoneAsync(done.Id, true); // выполненное без дня — по-прежнему нельзя (polish-3.md, S3)
        var project = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Ремонт", new DayTargetDto(ThisMonday, 0), IsProject: true));
        var step = await PostAsync<TaskDto>($"/api/tasks/{project.Id}/steps", new StepRequest("Шаг"));

        foreach (var (id, kind) in new[] { (thought.Id, "Missed"), (done.Id, "Missed"), (project.Id, "NotNeeded"), (step.Id, "NotNeeded"), (done.Id, "Maybe") })
        {
            var response = await Client.PutAsJsonAsync($"/api/tasks/{id}/skip", new SkipTaskRequest(kind));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Done_FromSkipped_ClearsSkipAndHistory()
    {
        var task = await CreateInDayAsync("Бег", ThisMonday);
        await SkipAsync(task.Id, "Missed", "не успела");
        var done = await SetTaskDoneAsync(task.Id, true);

        Assert.Equal(TaskStateDto.Done, done.State);
        Assert.Null(done.SkipKind);
        Assert.Empty(await HistoryAsync(task.Id));
    }

    [Fact]
    public async Task Done_InDay_Missed_UncompletesAndSkips_UndoWithDone_BackToDone()
    {
        var task = await CreateInDayAsync("Бег", ThisMonday);
        await SetTaskDoneAsync(task.Id, true);

        var result = await SkipAsync(task.Id, "Missed", "болезнь");
        Assert.True(result.WasDone);
        Assert.Equal((TaskStateDto.Skipped, "Missed", "болезнь"), (result.Task.State, result.Task.SkipKind, result.Task.SkipReason));
        Assert.Single(await HistoryAsync(task.Id));

        var back = await ReadAsync<TaskDto>(await Client.DeleteAsync($"/api/tasks/{task.Id}/skip?done=true"));
        Assert.Equal(TaskStateDto.Done, back.State);
        Assert.Null(back.SkipKind);
        Assert.Empty(await HistoryAsync(task.Id));
    }

    [Fact]
    public async Task Done_OnWeek_NotNeeded_Unskip_Open()
    {
        var task = await CreateInDayAsync("Позвонить", ThisMonday);
        await MoveTaskAsync(task.Id, new WeekTargetDto(ThisMonday));
        await SetTaskDoneAsync(task.Id, true);

        var result = await SkipAsync(task.Id, "NotNeeded");
        Assert.True(result.WasDone);
        Assert.Equal((TaskStateDto.Skipped, (DateOnly?)ThisMonday), (result.Task.State, result.Task.PlannedWeek));

        var open = await ReadAsync<TaskDto>(await Client.DeleteAsync($"/api/tasks/{task.Id}/skip")); // «Отменить пропуск» потом — открыто
        Assert.Equal(TaskStateDto.Planned, open.State);
    }

    [Fact]
    public async Task DoneHabitSession_Missed_ClipGone_GoalProgressDown_UndoBringsClipBack()
    {
        var goal = await GoalAsync("Тело", "Бег", 2, ThisMonday);
        var session = await SessionAsync(goal, ThisMonday);
        await SetTaskDoneAsync(session.Id, true);
        Assert.Equal(1, (await HabitAsync(goal.SeriesId)).Stats.Total);

        var result = await SkipAsync(session.Id, "Missed");
        Assert.True(result.WasDone);
        Assert.Equal(0, (await HabitAsync(goal.SeriesId)).Stats.Total);
        var week = (await GetWeekAsync(ThisMonday)).Goals.Single(g => g.Id == goal.Id);
        Assert.Equal((0, 2), (week.DoneCount, week.Norm));
        Assert.Equal("Missed", week.Sessions.Single(s => s.TaskId == session.Id).Skip);

        (await Client.DeleteAsync($"/api/tasks/{session.Id}/skip?done=true")).EnsureSuccessStatusCode();
        Assert.Equal(1, (await HabitAsync(goal.SeriesId)).Stats.Total);
    }

    [Fact]
    public async Task DoneStepOnDay_Missed_ReturnsToProjectList_ProjectStaysDone()
    {
        var project = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Ремонт", new DayTargetDto(ThisMonday, 0), IsProject: true));
        var step = await PostAsync<TaskDto>($"/api/tasks/{project.Id}/steps", new StepRequest("Плитка"));
        var day = ThisMonday.AddDays(2);
        await MoveTaskAsync(step.Id, new DayTargetDto(day, 0));
        await SetTaskDoneAsync(step.Id, true); // единственный шаг — проект отметился сам

        var result = await SkipAsync(step.Id, "Missed", "форс-мажор");
        Assert.True(result.WasDone);
        Assert.True(result.ReturnedToProject);
        Assert.Equal((TaskStateDto.Planned, (DateOnly?)null), (result.Task.State, result.Task.PlannedDate));
        Assert.Equal(TaskStateDto.Done, (await GetAsync<TaskDto>($"/api/tasks/{project.Id}")).State); // больше ничего само не меняется

        var back = await ReadAsync<TaskDto>(await Client.DeleteAsync($"/api/tasks/{step.Id}/skip?returnTo={day:yyyy-MM-dd}&done=true"));
        Assert.Equal((TaskStateDto.Done, (DateOnly?)day), (back.State, back.PlannedDate));
    }

    [Fact]
    public async Task Move_ToOtherDay_ReopensButKeepsHistory_SameDayKeepsSkip()
    {
        var task = await CreateInDayAsync("Бег", ThisMonday);
        await CreateInDayAsync("Ещё", ThisMonday);
        await SkipAsync(task.Id, "Missed", "заболела");

        var reordered = await MoveTaskAsync(task.Id, new DayTargetDto(ThisMonday, 1));
        Assert.Equal(TaskStateDto.Skipped, reordered.State);

        var moved = await MoveTaskAsync(task.Id, new DayTargetDto(ThisMonday.AddDays(1), 0));
        Assert.Equal(TaskStateDto.Planned, moved.State);
        Assert.Null(moved.SkipKind);
        Assert.Equal("заболела", Assert.Single(await HistoryAsync(task.Id)).Reason);
    }

    [Fact]
    public async Task Goal_NotNeeded_LowersNorm_Missed_DoesNot()
    {
        var goal = await GoalAsync("Тело", "Бег", 3, ThisMonday);
        var a = await SessionAsync(goal, ThisMonday);
        var b = await SessionAsync(goal, ThisMonday.AddDays(1));
        var c = await SessionAsync(goal, ThisMonday.AddDays(2));
        await SetTaskDoneAsync(a.Id, true);
        await SetTaskDoneAsync(b.Id, true);
        await SkipAsync(c.Id, "Missed", "забила");

        var week = await GetWeekAsync(ThisMonday);
        var missed = week.Goals.Single(g => g.Id == goal.Id);
        Assert.Equal((2, 3, false), (missed.DoneCount, missed.Norm, missed.IsDone));

        await SkipAsync(c.Id, "NotNeeded");
        var notNeeded = (await GetWeekAsync(ThisMonday)).Goals.Single(g => g.Id == goal.Id);
        Assert.Equal((2, 2, true, 1), (notNeeded.DoneCount, notNeeded.Norm, notNeeded.IsDone, notNeeded.NotNeededCount));
        Assert.Equal("NotNeeded", notNeeded.Sessions.Single(s => s.TaskId == c.Id).Skip);
    }

    [Fact]
    public async Task Habit_NotNeeded_IsNeitherClipNorMiss_WeekWithoutNorm_StreakSurvives()
    {
        var twoWeeksAgo = ThisMonday.AddDays(-14);
        var lastWeek = ThisMonday.AddDays(-7);
        var goal = await GoalAsync("Тело", "Бег", 2, twoWeeksAgo);
        await SetTaskDoneAsync((await SessionAsync(goal, twoWeeksAgo)).Id, true);
        await SkipAsync((await SessionAsync(goal, twoWeeksAgo.AddDays(1))).Id, "NotNeeded"); // норма 2 − 1 = 1 — неделя засчитана

        // Прошлая неделя: оба занятия «не нужно» — норма 0, неделя без нормы
        var last = (await GetWeekAsync(lastWeek)).Goals.Single(g => g.SeriesId == goal.SeriesId);
        await SkipAsync((await SessionAsync(last, lastWeek)).Id, "NotNeeded");
        await SkipAsync((await SessionAsync(last, lastWeek.AddDays(1))).Id, "NotNeeded");

        var habit = await HabitAsync(goal.SeriesId);
        Assert.Equal(1, habit.Stats.Total); // «не нужно» — не скрепка
        var weeks = habit.Stats.Weeks.TakeLast(3).ToList();
        Assert.Equal(PeriodResultDto.Done, weeks[0].Result);
        Assert.Equal(1, weeks[0].Norm);
        Assert.NotEqual(PeriodResultDto.Missed, weeks[1].Result);
        Assert.NotEqual(PeriodResultDto.Paused, weeks[1].Result);
    }

    [Fact]
    public async Task Habit_Missed_IsMiss()
    {
        // Первая неделя серии без нормы — не пропуск (habits.md), поэтому серия начинается выполненной неделей
        var twoWeeksAgo = ThisMonday.AddDays(-14);
        var lastWeek = ThisMonday.AddDays(-7);
        var goal = await GoalAsync("Тело", "Бег", 1, twoWeeksAgo);
        await SetTaskDoneAsync((await SessionAsync(goal, twoWeeksAgo)).Id, true);
        var last = (await GetWeekAsync(lastWeek)).Goals.Single(g => g.SeriesId == goal.SeriesId);
        await SkipAsync((await SessionAsync(last, lastWeek)).Id, "Missed", "заболела");

        var habit = await HabitAsync(goal.SeriesId);
        Assert.Equal(1, habit.Stats.Total); // «не сделано» — не скрепка
        Assert.Equal(PeriodResultDto.Paused, habit.Stats.Weeks[^2].Result); // первый пропуск — пауза
    }

    [Fact]
    public async Task Step_NotNeeded_NotCounted_AndCompletesProject()
    {
        var project = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Ремонт", new DayTargetDto(ThisMonday, 0), IsProject: true));
        var paint = await PostAsync<TaskDto>($"/api/tasks/{project.Id}/steps", new StepRequest("Покрасить"));
        var tiles = await PostAsync<TaskDto>($"/api/tasks/{project.Id}/steps", new StepRequest("Плитка"));
        await MoveTaskAsync(tiles.Id, new DayTargetDto(ThisMonday, 0));
        await SetTaskDoneAsync(paint.Id, true);

        var result = await SkipAsync(tiles.Id, "NotNeeded");
        Assert.NotNull(result.Parent);
        Assert.Equal(TaskStateDto.Done, result.Parent!.State); // S1: «не нужно» не мешает автоотметке

        var steps = await GetAsync<List<StepDto>>($"/api/tasks/{project.Id}/steps");
        Assert.Equal("NotNeeded", steps.Single(s => s.Id == tiles.Id).Skip);
    }

    [Fact]
    public async Task Step_MissedOnDay_ReturnsToProjectList_ReasonInHistory()
    {
        var project = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Ремонт", new DayTargetDto(ThisMonday, 0), IsProject: true));
        var step = await PostAsync<TaskDto>($"/api/tasks/{project.Id}/steps", new StepRequest("Плитка"));
        await MoveTaskAsync(step.Id, new DayTargetDto(ThisMonday.AddDays(2), 0));

        var result = await SkipAsync(step.Id, "Missed", "форс-мажор");

        Assert.True(result.ReturnedToProject);
        Assert.Equal(TaskStateDto.Planned, result.Task.State);
        Assert.Null(result.Task.PlannedDate);
        Assert.Null(result.Task.SkipKind);
        var history = Assert.Single(await HistoryAsync(step.Id));
        Assert.Equal(("Missed", "форс-мажор", ThisMonday.AddDays(2)), (history.Kind, history.Reason, history.Date));
        Assert.DoesNotContain((await GetWeekAsync(ThisMonday)).Tasks, t => t.Id == step.Id);
    }

    [Fact]
    public async Task Step_MissedReturned_Undo_BackToDay_HistoryGone()
    {
        var project = await PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest("Ремонт", new DayTargetDto(ThisMonday, 0), IsProject: true));
        var step = await PostAsync<TaskDto>($"/api/tasks/{project.Id}/steps", new StepRequest("Плитка"));
        var day = ThisMonday.AddDays(2);
        await MoveTaskAsync(step.Id, new DayTargetDto(day, 0));
        await SkipAsync(step.Id, "Missed");

        var back = await ReadAsync<TaskDto>(await Client.DeleteAsync($"/api/tasks/{step.Id}/skip?returnTo={day:yyyy-MM-dd}"));

        Assert.Equal((TaskStateDto.Planned, (DateOnly?)day), (back.State, back.PlannedDate));
        Assert.Empty(await HistoryAsync(step.Id));
    }

    [Fact]
    public async Task DeleteGoal_SkippedSessionsStayAsHistory()
    {
        var goal = await GoalAsync("Работа", "Отчёт", 2, ThisMonday, recurring: false);
        var session = await SessionAsync(goal, ThisMonday);
        await SkipAsync(session.Id, "Missed", "не успела");

        (await Client.DeleteAsync($"/api/goals/{goal.Id}")).EnsureSuccessStatusCode();

        var kept = await GetAsync<TaskDto>($"/api/tasks/{session.Id}");
        Assert.Equal((TaskStateDto.Skipped, (int?)null), (kept.State, kept.GoalId));
    }
}
