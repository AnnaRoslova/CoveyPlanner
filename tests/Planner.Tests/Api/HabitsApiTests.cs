using System.Net;
using System.Net.Http.Json;
using Planner.Core.Time;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Копилки привычек: скрепки — выполненные дела копилки (через цель или напрямую), «+1 сегодня», каникулы.</summary>
public class HabitsApiTests : ApiTestBase
{
    // Копилка создаётся «сейчас» — дела ставим на текущую неделю, чтобы они входили в её недели
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly DateOnly ThisMonday = Week.StartOf(Today);

    private Task<HabitDto> CreateHabitAsync(string name = "Тренировки", int norm = 3, int startCount = 0) =>
        PostAsync<HabitDto>("/api/habits", new SaveHabitRequest(name, "💪", null, norm, StartCount: startCount));

    private Task<HabitsDto> GetHabitsAsync() => GetAsync<HabitsDto>($"/api/habits?today={Today:yyyy-MM-dd}");

    private async Task<HabitDto> HabitAsync(int id) => (await GetHabitsAsync()).Habits.Single(h => h.Id == id);

    private async Task<WeeklyGoalDto> CreateGoalAsync(string role, string title, bool recurring = false)
    {
        var roleId = (await GetBoardAsync()).Roles.Single(r => r.Name == role).Id;
        return await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roleId, ThisMonday, title, recurring));
    }

    private Task<GoalUpdateResultDto> SetGoalHabitAsync(WeeklyGoalDto g, int habitId, int times = 3) =>
        PutAsync<GoalUpdateResultDto>($"/api/goals/{g.Id}",
            new UpdateGoalRequest(g.Title, g.RoleIds, times, g.StartTime, g.Quadrant, g.Notes, g.DurationMinutes, HabitId: habitId));

    private async Task<TaskDto> ScheduleAsync(WeeklyGoalDto goal, DateOnly day) =>
        (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(day, 0))).Task!;

    private Task<TaskDto> SetDoneAsync(int taskId, bool done) =>
        PutAsync<TaskDto>($"/api/tasks/{taskId}/done", new SetDoneRequest(done));

    [Fact]
    public async Task NewHabit_EmptyStats_DefaultsAndPaletteColor()
    {
        var habit = await CreateHabitAsync(startCount: 5);

        Assert.Equal((30, 5, 0, 5), (habit.JarSize, habit.Stats.Total, habit.Stats.FullJars, habit.Stats.InJar));
        Assert.Equal(26, habit.Stats.Weeks.Count);
        Assert.Equal(PeriodResultDto.Current, habit.Stats.Weeks[^1].Result);
        Assert.Contains(habit.Color, (await GetBoardAsync()).RolePalette);
    }

    [Fact]
    public async Task GoalWithHabit_SessionsInherit_DoneCounts_UndoneAndTrashedDoNot()
    {
        var habit = await CreateHabitAsync();
        var goal = await CreateGoalAsync("Тело", "Бег");
        Assert.Equal(habit.Id, (await SetGoalHabitAsync(goal, habit.Id)).Goal.HabitId);

        var first = await ScheduleAsync(goal, ThisMonday);
        var second = await ScheduleAsync(goal, ThisMonday.AddDays(2));
        Assert.Equal(habit.Id, first.HabitId);

        await SetDoneAsync(first.Id, true);
        await SetDoneAsync(second.Id, true);
        var stats = (await HabitAsync(habit.Id)).Stats;
        Assert.Equal((2, 2), (stats.Total, stats.ThisWeek));

        // Сняли галочку — скрепка ушла; в корзине — не считается
        await SetDoneAsync(second.Id, false);
        (await Client.DeleteAsync($"/api/tasks/{first.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await HabitAsync(habit.Id)).Stats.Total);

        // Вернули из корзины — снова скрепка
        await PostAsync<TaskDto>($"/api/trash/{first.Id}/restore", new { });
        Assert.Equal(1, (await HabitAsync(habit.Id)).Stats.Total);
    }

    [Fact]
    public async Task GoalCheckbox_NewDoneSession_IsAClip()
    {
        var habit = await CreateHabitAsync();
        var goal = await CreateGoalAsync("Ум", "Чтение");
        await SetGoalHabitAsync(goal, habit.Id);

        var result = await PostAsync<GoalResultDto>($"/api/goals/{goal.Id}/toggle", new ToggleGoalRequest(Today));

        Assert.Equal(habit.Id, result.Task!.HabitId);
        Assert.Equal(1, (await HabitAsync(habit.Id)).Stats.ThisWeek);
    }

    [Fact]
    public async Task LinkingGoalLater_ItsDoneSessionsMoveToHabit_ChangingHabitMovesThem()
    {
        var goal = await CreateGoalAsync("Тело", "Бег");
        var session = await ScheduleAsync(goal, ThisMonday);
        await SetDoneAsync(session.Id, true);

        var a = await CreateHabitAsync("A");
        var b = await CreateHabitAsync("B");
        await SetGoalHabitAsync(goal, a.Id);
        Assert.Equal(1, (await HabitAsync(a.Id)).Stats.Total);

        await SetGoalHabitAsync(goal, b.Id);
        Assert.Equal((0, 1), ((await HabitAsync(a.Id)).Stats.Total, (await HabitAsync(b.Id)).Stats.Total));

        // 0 — без копилки
        Assert.Null((await SetGoalHabitAsync(goal, 0)).Goal.HabitId);
        Assert.Equal(0, (await HabitAsync(b.Id)).Stats.Total);
    }

    [Fact]
    public async Task RecurringGoal_NextWeekGoalAndItsSessionsKeepHabit()
    {
        var habit = await CreateHabitAsync();
        var goal = await CreateGoalAsync("Тело", "Бег", recurring: true);
        await PutAsync<GoalUpdateResultDto>($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, 2, null, null, null, null, IsRecurring: true, Days: 1 | 4, HabitId: habit.Id));

        var next = await GetWeekAsync(ThisMonday.AddDays(7));
        var nextGoal = next.Goals.Single(g => g.Title == "Бег");
        Assert.Equal(habit.Id, nextGoal.HabitId);
        Assert.All(next.Tasks.Where(t => t.GoalId == nextGoal.Id), t => Assert.Equal(habit.Id, t.HabitId));
    }

    [Fact]
    public async Task Task_OwnHabit_SetAndClear()
    {
        var habit = await CreateHabitAsync();
        var task = await CreateInDayAsync("Зарядка", Today);

        var linked = await PutAsync<TaskDto>($"/api/tasks/{task.Id}",
            new UpdateTaskRequest(task.Title, null, [], null, null, null, habit.Id));
        Assert.Equal(habit.Id, linked.HabitId);

        // HabitId = null — не менять
        var same = await PutAsync<TaskDto>($"/api/tasks/{task.Id}", new UpdateTaskRequest("Зарядка!", null, [], null, null, null));
        Assert.Equal(habit.Id, same.HabitId);

        var cleared = await PutAsync<TaskDto>($"/api/tasks/{task.Id}", new UpdateTaskRequest("Зарядка", null, [], null, null, null, 0));
        Assert.Null(cleared.HabitId);

        var missing = await Client.PutAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Зарядка", null, [], null, null, null, 999));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PlusOne_DoneTaskAtEndOfToday_TotalGrows()
    {
        var habit = await CreateHabitAsync(startCount: 29);
        await CreateInDayAsync("Утро", Today);

        var result = await PostAsync<PlusOneResultDto>($"/api/habits/{habit.Id}/plus-one", new PlusOneRequest(Today));

        Assert.Equal(("Тренировки", TaskStateDto.Done, Today, habit.Id), (result.Task.Title, result.Task.State, result.Task.PlannedDate!.Value, result.Task.HabitId!.Value));
        Assert.Equal((30, 1, 0, 1), (result.Habit.Stats.Total, result.Habit.Stats.FullJars, result.Habit.Stats.InJar, result.Habit.Stats.ThisWeek));
        var day = (await GetWeekAsync(Today)).Tasks.Where(t => t.PlannedDate == Today).OrderBy(t => t.SortOrder).Select(t => t.Title);
        Assert.Equal(["Утро", "Тренировки"], day);
    }

    [Fact]
    public async Task Update_ArchiveAndValidation()
    {
        var habit = await CreateHabitAsync();

        var updated = await PutAsync<HabitDto>($"/api/habits/{habit.Id}",
            new SaveHabitRequest("Спорт", "🏃", "#a9cde8", 4, 50, 10, IsArchived: true));
        Assert.Equal(("Спорт", "🏃", "#a9cde8", 4, 50, 10, true),
            (updated.Name, updated.Emoji, updated.Color, updated.WeeklyNorm, updated.JarSize, updated.StartCount, updated.IsArchived));

        var bad = await Client.PutAsJsonAsync($"/api/habits/{habit.Id}", new SaveHabitRequest("Спорт", null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var archivedPlus = await Client.PostAsJsonAsync($"/api/habits/{habit.Id}/plus-one", new PlusOneRequest(Today));
        Assert.Equal(HttpStatusCode.BadRequest, archivedPlus.StatusCode);
    }

    [Fact]
    public async Task Vacations_CreateListDelete_WeekBecomesVacation()
    {
        var habit = await CreateHabitAsync();
        var other = await CreateHabitAsync("Чтение");
        var lastWeek = ThisMonday.AddDays(-7);

        var vacation = await PostAsync<VacationDto>("/api/vacations", new CreateVacationRequest(lastWeek, lastWeek.AddDays(6), [habit.Id]));
        var all = await GetHabitsAsync();
        Assert.Equal([vacation.Id], all.Vacations.Select(v => v.Id));
        Assert.Equal([habit.Id], all.Vacations[0].HabitIds);

        // Копилка создана на этой неделе — прошлая для неё «до создания», каникулы её не меняют
        Assert.Equal(PeriodResultDto.BeforeStart, all.Habits.Single(h => h.Id == habit.Id).Stats.Weeks[^2].Result);

        var bad = await Client.PostAsJsonAsync("/api/vacations", new CreateVacationRequest(Today, Today.AddDays(-1), null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        (await Client.DeleteAsync($"/api/vacations/{vacation.Id}")).EnsureSuccessStatusCode();
        Assert.Empty((await GetHabitsAsync()).Vacations);
        Assert.NotNull(other);
    }

    [Fact]
    public async Task Today_InVacation_VacationUntil()
    {
        var habit = await CreateHabitAsync();
        await PostAsync<VacationDto>("/api/vacations", new CreateVacationRequest(Today, Today.AddDays(5), null));

        Assert.Equal(Today.AddDays(5), (await HabitAsync(habit.Id)).Stats.VacationUntil);
    }

    [Fact]
    public async Task Delete_TasksAndGoalsStay_OwnVacationsGo_SharedKeepOthers()
    {
        var habit = await CreateHabitAsync();
        var other = await CreateHabitAsync("Чтение");
        var goal = await CreateGoalAsync("Тело", "Бег");
        await SetGoalHabitAsync(goal, habit.Id);
        var session = await ScheduleAsync(goal, Today);
        await SetDoneAsync(session.Id, true);
        await PostAsync<VacationDto>("/api/vacations", new CreateVacationRequest(Today, Today, [habit.Id]));
        var shared = await PostAsync<VacationDto>("/api/vacations", new CreateVacationRequest(Today, Today, [habit.Id, other.Id]));

        (await Client.DeleteAsync($"/api/habits/{habit.Id}")).EnsureSuccessStatusCode();

        var week = await GetWeekAsync(Today);
        Assert.Null(week.Goals.Single().HabitId);
        Assert.Null(week.Tasks.Single(t => t.Id == session.Id).HabitId);
        var vacations = (await GetHabitsAsync()).Vacations;
        Assert.Equal([shared.Id], vacations.Select(v => v.Id));
        Assert.Equal([other.Id], vacations[0].HabitIds);
    }

    [Fact]
    public async Task UnknownHabit_ForGoal_404()
    {
        var goal = await CreateGoalAsync("Тело", "Бег");

        var response = await Client.PutAsJsonAsync($"/api/goals/{goal.Id}",
            new UpdateGoalRequest(goal.Title, goal.RoleIds, 1, null, null, null, null, HabitId: 42));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
