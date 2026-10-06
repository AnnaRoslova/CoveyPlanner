using System.Net;
using System.Net.Http.Json;
using Planner.Core.Time;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Привычки = постоянные цели «Затачивания пилы»: банка у серии, скрепки — выполненные занятия, норма — «раз в неделю» каждой
/// недели; одна история через все правки цели; архив при выключении повтора; «+1 сегодня»; каникулы; удаление из архива.
/// </summary>
public class HabitsApiTests : ApiTestBase
{
    // Сервер считает «сегодня» по часам компьютера — цели ставим на текущую и прошлые недели
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly DateOnly ThisMonday = Week.StartOf(Today);

    private Task<HabitsDto> GetHabitsAsync() => GetAsync<HabitsDto>($"/api/habits?today={Today:yyyy-MM-dd}");

    private async Task<HabitDto?> HabitAsync(int? seriesId) => (await GetHabitsAsync()).Habits.SingleOrDefault(h => h.Id == seriesId);

    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private async Task<WeeklyGoalDto> CreateGoalAsync(string role, string title, bool recurring = true, DateOnly? week = null) =>
        await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync(role), week ?? ThisMonday, title, recurring));

    private async Task<WeeklyGoalDto> UpdateGoalAsync(
        WeeklyGoalDto g, int? times = null, IReadOnlyList<int>? roleIds = null, bool? recurring = null, int? days = null, TimeOnly? time = null) =>
        (await PutAsync<GoalUpdateResultDto>($"/api/goals/{g.Id}",
            new UpdateGoalRequest(g.Title, roleIds ?? g.RoleIds, times ?? g.TimesPerWeek, time ?? g.StartTime, g.Quadrant, g.Notes,
                g.DurationMinutes, recurring, Days: days))).Goal;

    private async Task<TaskDto> DoneSessionAsync(WeeklyGoalDto goal, DateOnly day)
    {
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(day, 0))).Task!;
        return await PutAsync<TaskDto>($"/api/tasks/{session.Id}/done", new SetDoneRequest(true));
    }

    /// <summary>Цель этой же серии на неделе <paramref name="week"/> (неделю открывают — цели постоянных серий появляются).</summary>
    private async Task<WeeklyGoalDto> GoalOnWeekAsync(WeeklyGoalDto goal, DateOnly week) =>
        (await GetWeekAsync(week)).Goals.Single(g => g.SeriesId == goal.SeriesId);

    [Fact]
    public async Task RecurringSawGoal_IsHabit_OneOffAndOrdinaryRoles_AreNot()
    {
        var run = await CreateGoalAsync("Тело", "Бег");
        await CreateGoalAsync("Тело", "Марафон", recurring: false);
        await CreateGoalAsync("Работа", "Планёрка");

        var habit = Assert.Single((await GetHabitsAsync()).Habits);
        var body = (await GetBoardAsync()).Roles.Single(r => r.Name == "Тело");
        Assert.Equal((run.SeriesId!.Value, "Бег", body.Color, 1, 30, 0, false),
            (habit.Id, habit.Name, habit.Color, habit.WeeklyNorm, habit.JarSize, habit.StartCount, habit.IsArchived));
        Assert.Equal([body.Id], habit.DimensionRoleIds);
        Assert.Equal((0, PeriodResultDto.Current), (habit.Stats.Total, habit.Stats.Weeks[^1].Result));
    }

    [Fact]
    public async Task DoneSessions_AreClips_UndoneAndTrashedAreNot()
    {
        var goal = await UpdateGoalAsync(await CreateGoalAsync("Тело", "Бег"), times: 3, time: null);
        var a = await DoneSessionAsync(goal, ThisMonday);
        await DoneSessionAsync(goal, ThisMonday.AddDays(1));
        await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(ThisMonday.AddDays(2), 0));
        Assert.Equal(2, (await HabitAsync(goal.SeriesId))!.Stats.Total);

        (await Client.DeleteAsync($"/api/tasks/{a.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(1, (await HabitAsync(goal.SeriesId))!.Stats.ThisWeek);

        // Сняли отметку — скрепка ушла
        var b = (await GetWeekAsync(ThisMonday)).Tasks.Single(t => t.State == TaskStateDto.Done);
        await PutAsync<TaskDto>($"/api/tasks/{b.Id}/done", new SetDoneRequest(false));
        Assert.Equal(0, (await HabitAsync(goal.SeriesId))!.Stats.Total);
    }

    [Fact]
    public async Task History_SurvivesGoalEdits_PastWeeksKeepTheirNorm()
    {
        var twoWeeksAgo = ThisMonday.AddDays(-14);
        var first = await UpdateGoalAsync(await CreateGoalAsync("Тело", "Бег", week: twoWeeksAgo), times: 2);
        await DoneSessionAsync(first, twoWeeksAgo);
        await DoneSessionAsync(first, twoWeeksAgo.AddDays(2));

        // Прошлая неделя: норму снизили до 1 «с этой недели и дальше», одно занятие
        var lastWeek = await GoalOnWeekAsync(first, ThisMonday.AddDays(-7));
        lastWeek = await UpdateGoalAsync(lastWeek, times: 1);
        await DoneSessionAsync(lastWeek, ThisMonday.AddDays(-7));

        // Эта неделя: другие дни, время и ещё одно измерение — та же привычка, история на месте
        var current = await GoalOnWeekAsync(first, ThisMonday);
        var body = await RoleIdAsync("Тело");
        var spirit = await RoleIdAsync("Дух");
        await UpdateGoalAsync(current, roleIds: [body, spirit], days: 1 | 4, time: new TimeOnly(7, 30));

        var habit = (await HabitAsync(first.SeriesId))!;
        Assert.Equal((3, 2, 2), (habit.Stats.Total, habit.Stats.WeekStreak, habit.WeeklyNorm));
        Assert.Equal([2, 1, 2], habit.Stats.Weeks.TakeLast(3).Select(w => w.Norm!.Value));
        Assert.Equal([PeriodResultDto.Done, PeriodResultDto.Done, PeriodResultDto.Current], habit.Stats.Weeks.TakeLast(3).Select(w => w.Result));
        Assert.Equal([body, spirit], habit.DimensionRoleIds);
        Assert.Equal(PeriodResultDto.BeforeStart, habit.Stats.Weeks[^4].Result);
    }

    [Fact]
    public async Task StopRecurring_HabitToArchiveWithHistory_ResumeContinues()
    {
        var lastWeek = ThisMonday.AddDays(-7);
        var first = await CreateGoalAsync("Ум", "Чтение", week: lastWeek);
        await DoneSessionAsync(first, lastWeek);
        var current = await GoalOnWeekAsync(first, ThisMonday);

        current = await UpdateGoalAsync(current, recurring: false);
        var archived = (await HabitAsync(first.SeriesId))!;
        Assert.Equal((true, 1, 1), (archived.IsArchived, archived.Stats.Total, archived.Stats.WeekStreak));

        await UpdateGoalAsync(current, recurring: true);
        var resumed = (await HabitAsync(first.SeriesId))!;
        Assert.Equal((false, 1, 1), (resumed.IsArchived, resumed.Stats.Total, resumed.Stats.WeekStreak));
    }

    [Fact]
    public async Task EndedSeries_WithoutClips_Hidden_WithCustomJar_InArchive()
    {
        var empty = await CreateGoalAsync("Дух", "Тишина");
        var jarred = await CreateGoalAsync("Дух", "Дыхание");
        await PutAsync<HabitDto>($"/api/habits/{jarred.SeriesId}", new UpdateHabitRequest(StartCount: 12, JarSize: 30));

        (await Client.DeleteAsync($"/api/goals/{empty.Id}?scope=series")).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/goals/{jarred.Id}?scope=series")).EnsureSuccessStatusCode();

        var habits = (await GetHabitsAsync()).Habits;
        var only = Assert.Single(habits);
        Assert.Equal((jarred.SeriesId!.Value, true, 12), (only.Id, only.IsArchived, only.Stats.Total));
    }

    [Fact]
    public async Task Jar_Update_Validation_OnlyHabits()
    {
        var goal = await CreateGoalAsync("Тело", "Бег");

        var updated = await PutAsync<HabitDto>($"/api/habits/{goal.SeriesId}", new UpdateHabitRequest(29, 10));
        Assert.Equal((10, 29, 29, 2, 9), (updated.JarSize, updated.StartCount, updated.Stats.Total, updated.Stats.FullJars, updated.Stats.InJar));

        var bad = await Client.PutAsJsonAsync($"/api/habits/{goal.SeriesId}", new UpdateHabitRequest(0, 0));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Постоянная цель обычной роли — не привычка
        var work = await CreateGoalAsync("Работа", "Планёрка");
        var notHabit = await Client.PutAsJsonAsync($"/api/habits/{work.SeriesId}", new UpdateHabitRequest(0, 30));
        Assert.Equal(HttpStatusCode.NotFound, notHabit.StatusCode);
    }

    [Fact]
    public async Task PlusOne_DoneSessionToday_OncePerDay()
    {
        var goal = await UpdateGoalAsync(await CreateGoalAsync("Тело", "Бег"), times: 3);
        await CreateInDayAsync("Утро", Today);

        var result = await PostAsync<PlusOneResultDto>($"/api/habits/{goal.SeriesId}/plus-one", new PlusOneRequest(Today));

        Assert.Equal(("Бег", TaskStateDto.Done, Today, goal.Id), (result.Task.Title, result.Task.State, result.Task.PlannedDate!.Value, result.Task.GoalId!.Value));
        Assert.Equal((1, 1), (result.Goal.DoneCount, result.Habit.Stats.ThisWeek));
        var day = (await GetWeekAsync(ThisMonday)).Tasks.Where(t => t.PlannedDate == Today).OrderBy(t => t.SortOrder).Select(t => t.Title);
        Assert.Equal(["Утро", "Бег"], day);

        // Одно занятие в день — как у галочки цели
        var again = await Client.PostAsJsonAsync($"/api/habits/{goal.SeriesId}/plus-one", new PlusOneRequest(Today));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task PlusOne_OpenSessionToday_IsChecked_NoNewTask()
    {
        var goal = await CreateGoalAsync("Тело", "Бег");
        var session = (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(Today, 0))).Task!;

        var result = await PostAsync<PlusOneResultDto>($"/api/habits/{goal.SeriesId}/plus-one", new PlusOneRequest(Today));

        Assert.Equal((session.Id, TaskStateDto.Done), (result.Task.Id, result.Task.State));
        Assert.Single((await GetWeekAsync(ThisMonday)).Tasks);
    }

    [Fact]
    public async Task PlusOne_Archived_400()
    {
        var goal = await CreateGoalAsync("Тело", "Бег");
        await UpdateGoalAsync(goal, recurring: false);

        var response = await Client.PostAsJsonAsync($"/api/habits/{goal.SeriesId}/plus-one", new PlusOneRequest(Today));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Vacations_CreateListDelete_TodayInVacation()
    {
        var goal = await CreateGoalAsync("Тело", "Бег");

        var vacation = await PostAsync<VacationDto>("/api/vacations", new CreateVacationRequest(Today, Today.AddDays(5), [goal.SeriesId!.Value]));
        var all = await GetHabitsAsync();
        Assert.Equal([vacation.Id], all.Vacations.Select(v => v.Id));
        Assert.Equal([goal.SeriesId.Value], all.Vacations[0].HabitIds);
        Assert.Equal(Today.AddDays(5), all.Habits.Single().Stats.VacationUntil);

        var bad = await Client.PostAsJsonAsync("/api/vacations", new CreateVacationRequest(Today, Today.AddDays(-1), null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var unknown = await Client.PostAsJsonAsync("/api/vacations", new CreateVacationRequest(Today, Today, [999]));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        (await Client.DeleteAsync($"/api/vacations/{vacation.Id}")).EnsureSuccessStatusCode();
        Assert.Empty((await GetHabitsAsync()).Vacations);
    }

    [Fact]
    public async Task Delete_OnlyFromArchive_CountGone_GoalAndTasksStay_ResumeBringsItBack()
    {
        var goal = await CreateGoalAsync("Тело", "Бег");
        var other = await CreateGoalAsync("Ум", "Чтение");
        var session = await DoneSessionAsync(goal, ThisMonday);
        await PutAsync<HabitDto>($"/api/habits/{goal.SeriesId}", new UpdateHabitRequest(5, 20));
        await PostAsync<VacationDto>("/api/vacations", new CreateVacationRequest(Today, Today, [goal.SeriesId!.Value]));
        var shared = await PostAsync<VacationDto>("/api/vacations", new CreateVacationRequest(Today, Today, [goal.SeriesId.Value, other.SeriesId!.Value]));

        // Идущую привычку удалить нельзя — только из архива
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.DeleteAsync($"/api/habits/{goal.SeriesId}")).StatusCode);
        var stopped = await UpdateGoalAsync(goal, recurring: false);
        (await Client.DeleteAsync($"/api/habits/{goal.SeriesId}")).EnsureSuccessStatusCode();

        var habits = await GetHabitsAsync();
        Assert.DoesNotContain(habits.Habits, h => h.Id == goal.SeriesId);
        Assert.Equal([shared.Id], habits.Vacations.Select(v => v.Id));
        Assert.Equal([other.SeriesId.Value], habits.Vacations[0].HabitIds);
        var week = await GetWeekAsync(ThisMonday);
        Assert.Contains(week.Goals, g => g.Id == goal.Id);
        Assert.Equal(TaskStateDto.Done, week.Tasks.Single(t => t.Id == session.Id).State);

        // Снова «каждую неделю» — привычка вернулась: скрепки — те же занятия, банка — новая
        await UpdateGoalAsync(stopped, recurring: true);
        var back = (await HabitAsync(goal.SeriesId))!;
        Assert.Equal((1, 0, 30), (back.Stats.Total, back.StartCount, back.JarSize));
    }

    [Fact]
    public async Task SeveralDimensions_OrderedByRoles_GroupedByFirst()
    {
        var spirit = await RoleIdAsync("Дух");
        var body = await RoleIdAsync("Тело");
        var walk = await CreateGoalAsync("Дух", "Прогулка");
        await UpdateGoalAsync(walk, roleIds: [spirit, body]);
        await CreateGoalAsync("Ум", "Чтение");

        var habits = (await GetHabitsAsync()).Habits;
        Assert.Equal(["Прогулка", "Чтение"], habits.Select(h => h.Name)); // Тело раньше Ума
        Assert.Equal([body, spirit], habits[0].DimensionRoleIds);
    }
}
