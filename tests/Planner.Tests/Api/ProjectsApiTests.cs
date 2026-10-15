using System.Net;
using System.Net.Http.Json;
using Planner.Core.Time;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Дела, шаги и проекты (docs/specs/tasks-steps-done.md): шаг — полноценное дело в днях, «Проект» в карточке,
/// автоотметка родителя, корзина проекта целиком, проект в шаблоне цели (P), скрепки удалённых целей (C).
/// </summary>
public class ProjectsApiTests : ApiTestBase
{
    private static readonly DateOnly Thursday = Monday.AddDays(3);
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly DateOnly ThisMonday = Week.StartOf(Today);

    private Task<StepDto> AddStepAsync(int taskId, string title) =>
        PostAsync<StepDto>($"/api/tasks/{taskId}/steps", new StepRequest(title));

    private Task<List<StepDto>> StepsAsync(int taskId) => GetAsync<List<StepDto>>($"/api/tasks/{taskId}/steps");

    private Task<TaskDto> SetDoneAsync(int id, bool isDone = true) =>
        SetTaskDoneAsync(id, isDone);

    private Task<TaskDto> SetParentAsync(int id, int? parentId) =>
        PutAsync<TaskDto>($"/api/tasks/{id}/parent", new SetParentRequest(parentId));

    private async Task<TaskDto> TaskAsync(int id, DateOnly? week = null) =>
        (await GetWeekAsync(week)).Tasks.Single(t => t.Id == id);

    private async Task<int> RoleIdAsync(string name) => (await GetBoardAsync()).Roles.Single(r => r.Name == name).Id;

    private async Task<WeeklyGoalDto> CreateGoalAsync(string role, string title, DateOnly week, bool? recurring = null) =>
        await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(await RoleIdAsync(role), week, title, recurring));

    private async Task<WeeklyGoalDto> SetGoalProjectAsync(WeeklyGoalDto g, int projectId) =>
        (await PutAsync<GoalUpdateResultDto>($"/api/goals/{g.Id}",
            new UpdateGoalRequest(g.Title, g.RoleIds, g.TimesPerWeek, g.StartTime, g.Quadrant, g.Notes, g.DurationMinutes,
                ProjectId: projectId))).Goal;

    private async Task<TaskDto> ScheduleAsync(WeeklyGoalDto goal, DateOnly day) =>
        (await PostTargetAsync<GoalResultDto>($"/api/goals/{goal.Id}/schedule", new DayTargetDto(day, 0))).Task!;

    private async Task<int> HabitTotalAsync(int? seriesId) =>
        (await GetAsync<HabitsDto>($"/api/habits?today={Today:yyyy-MM-dd}")).Habits.Single(h => h.Id == seriesId).Stats.Total;

    // ---------- S: шаги в днях ----------

    [Fact]
    public async Task S2_S11_S19_StepGoesToDay_StaysInParentList_DoneIsShared()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var paint = await AddStepAsync(repair.Id, "Покрасить");
        await AddStepAsync(repair.Id, "Полка");

        var moved = await MoveTaskAsync(paint.Id, new DayTargetDto(Thursday, 0));
        Assert.Equal((Thursday, repair.Id), (moved.PlannedDate, moved.ParentId));

        var week = await GetWeekAsync();
        Assert.Contains(week.Tasks, t => t.Id == paint.Id && t.PlannedDate == Thursday);
        Assert.Equal(["Покрасить", "Полка"], (await StepsAsync(repair.Id)).Select(s => s.Title));

        await SetDoneAsync(paint.Id);
        Assert.True((await StepsAsync(repair.Id))[0].IsDone);
    }

    [Fact]
    public async Task S5_NewStep_CopiesParentRoles()
    {
        var work = await RoleIdAsync("Работа");
        var repair = await UpdateTaskAsync(await CreateInDayAsync("Ремонт"), roleIds: [work]);
        var step = await AddStepAsync(repair.Id, "Покрасить");
        var moved = await MoveTaskAsync(step.Id, new DayTargetDto(Thursday, 0));
        Assert.Equal([work], moved.RoleIds);
    }

    [Fact]
    public async Task S12_S13_LastStepDone_ParentDone_NeverReopensItself()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var a = await AddStepAsync(repair.Id, "Обои");
        var b = await AddStepAsync(repair.Id, "Плинтус");

        await SetDoneAsync(a.Id);
        Assert.Equal(TaskStateDto.Planned, (await TaskAsync(repair.Id)).State);
        await PutAsync<StepDto>($"/api/steps/{b.Id}/done", new SetDoneRequest(true));
        Assert.Equal(TaskStateDto.Done, (await TaskAsync(repair.Id)).State);

        // Сняли галочку с шага или добавили шаг — родитель остаётся выполненным
        await SetDoneAsync(a.Id, false);
        await AddStepAsync(repair.Id, "Люстра");
        Assert.Equal(TaskStateDto.Done, (await TaskAsync(repair.Id)).State);
    }

    /// <summary>
    /// S13 (баг ручной проверки этапа 6): у занятия цели с шагами отметка и снятие отдельных шагов, удаление шагов
    /// состояние самого дела не меняют — ни открытого, ни уже отмеченного.
    /// </summary>
    [Fact]
    public async Task S13_SessionWithSteps_StepToggleAndDelete_NeverChangeParent()
    {
        var goal = await CreateGoalAsync("Работа", "Подготовка к собесу", Monday, recurring: false);
        var session = await ScheduleAsync(goal, Thursday);
        var a = await AddStepAsync(session.Id, "Задачи");
        var b = await AddStepAsync(session.Id, "Резюме");

        await SetDoneAsync(a.Id);
        await SetDoneAsync(a.Id, false);
        await SetDoneAsync(a.Id);
        Assert.Equal(TaskStateDto.Planned, (await TaskAsync(session.Id)).State);

        (await Client.DeleteAsync($"/api/tasks/{a.Id}")).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/tasks/{b.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(TaskStateDto.Planned, (await TaskAsync(session.Id)).State);

        // Отмеченное дело: шаги его не открывают
        await SetDoneAsync(session.Id);
        var c = await AddStepAsync(session.Id, "Вопросы");
        await SetDoneAsync(c.Id);
        await SetDoneAsync(c.Id, false);
        (await Client.DeleteAsync($"/api/tasks/{c.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(TaskStateDto.Done, (await TaskAsync(session.Id)).State);
    }

    [Fact]
    public async Task S15_S16_TrashLastOpenStep_NoAutoDone_ManualDoneKeepsStepsOpen()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var a = await AddStepAsync(repair.Id, "Обои");
        var b = await AddStepAsync(repair.Id, "Плинтус");
        await SetDoneAsync(a.Id);
        (await Client.DeleteAsync($"/api/tasks/{b.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(TaskStateDto.Planned, (await TaskAsync(repair.Id)).State);

        var shed = await CreateInDayAsync("Сарай");
        var c = await AddStepAsync(shed.Id, "Доски");
        await SetDoneAsync(shed.Id);
        Assert.False((await StepsAsync(shed.Id)).Single(s => s.Id == c.Id).IsDone);
    }

    // ---------- S: «Проект» в карточке ----------

    [Fact]
    public async Task S8_S9_FeedThoughtBecomesStep_LeavesFeed_BackToItsCategory()
    {
        var ideas = await CreateCategoryAsync();
        var repair = await CreateTaskAsync("Ремонт", new SomedayTargetDto(ideas.Id, 0));
        var paint = await CreateTaskAsync("Купить краску", new SomedayTargetDto(ideas.Id, 1));

        var step = await SetParentAsync(paint.Id, repair.Id);
        Assert.Equal((TaskStateDto.Planned, (DateOnly?)null, repair.Id), (step.State, step.PlannedDate, step.ParentId));
        Assert.Equal(["Ремонт"], (await GetBoardAsync()).SomedayTasks.Select(t => t.Title));

        var back = await SetParentAsync(paint.Id, null);
        Assert.Equal((TaskStateDto.Someday, ideas.Id, (int?)null), (back.State, back.CategoryId, back.ParentId));
    }

    [Fact]
    public async Task S7_OneLevel_NoSelf_NoStepAsParent()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var step = await AddStepAsync(repair.Id, "Обои");
        var shed = await CreateInDayAsync("Сарай");

        foreach (var (id, parentId) in new[] { (repair.Id, shed.Id), (shed.Id, step.Id), (shed.Id, shed.Id) })
        {
            var response = await Client.PutAsJsonAsync($"/api/tasks/{id}/parent", new SetParentRequest(parentId));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task N2_4_SessionIsNeverParent()
    {
        var goal = await CreateGoalAsync("Работа", "Отчёт", Monday);
        var session = await ScheduleAsync(goal, Thursday);
        var call = await CreateInDayAsync("Звонок");

        var response = await Client.PutAsJsonAsync($"/api/tasks/{call.Id}/parent", new SetParentRequest(session.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task S10_ChangeProject_EndOfNewList_DayKept()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var shed = await CreateInDayAsync("Сарай");
        await AddStepAsync(shed.Id, "Доски");
        var paint = await AddStepAsync(repair.Id, "Покрасить");
        await MoveTaskAsync(paint.Id, new DayTargetDto(Thursday, 0));

        var moved = await SetParentAsync(paint.Id, shed.Id);
        Assert.Equal((shed.Id, Thursday), (moved.ParentId!.Value, moved.PlannedDate!.Value));
        Assert.Equal(["Доски", "Покрасить"], (await StepsAsync(shed.Id)).Select(s => s.Title));
        Assert.Empty(await StepsAsync(repair.Id));
    }

    // ---------- S: перенос ----------

    [Fact]
    public async Task S20_S21_StepDroppedOnParent_Undated_DroppedInFeed_BecomesThought()
    {
        var ideas = await CreateCategoryAsync();
        var repair = await CreateTaskAsync("Ремонт", new SomedayTargetDto(ideas.Id, 0));
        var paint = await AddStepAsync(repair.Id, "Покрасить");
        var walls = await AddStepAsync(repair.Id, "Стены");
        await MoveTaskAsync(paint.Id, new DayTargetDto(Thursday, 0));

        var undated = await MoveTaskAsync(paint.Id, new StepListTargetDto(repair.Id, 1));
        Assert.Equal(((DateOnly?)null, repair.Id), (undated.PlannedDate, undated.ParentId!.Value));
        Assert.Equal(["Стены", "Покрасить"], (await StepsAsync(repair.Id)).Select(s => s.Title));

        var thought = await MoveTaskAsync(walls.Id, new SomedayTargetDto(null, 0));
        Assert.Equal((TaskStateDto.Someday, (int?)null), (thought.State, thought.ParentId));
        Assert.Equal(["Покрасить"], (await StepsAsync(repair.Id)).Select(s => s.Title));
    }

    // ---------- S: корзина ----------

    [Fact]
    public async Task K1_S23_S24_ParentTrashed_WithStepsInDays_RestoredTogether()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var done = await AddStepAsync(repair.Id, "Обои");
        var open = await AddStepAsync(repair.Id, "Плинтус");
        var undated = await AddStepAsync(repair.Id, "Люстра");
        await MoveTaskAsync(done.Id, new DayTargetDto(Monday, 0));
        await SetDoneAsync(done.Id);
        await MoveTaskAsync(open.Id, new DayTargetDto(Thursday, 0));

        (await Client.DeleteAsync($"/api/tasks/{repair.Id}")).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await GetWeekAsync()).Tasks, t => t.Title is "Обои" or "Плинтус");
        var trash = await GetAsync<List<TaskDto>>("/api/trash");
        Assert.Equal([repair.Id], trash.Select(t => t.Id)); // шаги — вместе с проектом, своей строки нет

        await PostAsync<TaskDto>($"/api/trash/{repair.Id}/restore", new { });
        var week = await GetWeekAsync();
        Assert.Equal(Monday, week.Tasks.Single(t => t.Id == done.Id).PlannedDate);
        Assert.Equal(TaskStateDto.Done, week.Tasks.Single(t => t.Id == done.Id).State);
        Assert.Equal(Thursday, week.Tasks.Single(t => t.Id == open.Id).PlannedDate);
        Assert.Equal(3, (await StepsAsync(repair.Id)).Count);
        Assert.Empty(await GetAsync<List<TaskDto>>("/api/trash"));
        _ = undated;
    }

    [Fact]
    public async Task K2_S25_S26_StepTrashedAlone_InTrashList_RestoreBringsParentBack()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var a = await AddStepAsync(repair.Id, "Обои");
        var b = await AddStepAsync(repair.Id, "Плинтус");

        (await Client.DeleteAsync($"/api/tasks/{a.Id}")).EnsureSuccessStatusCode();
        Assert.Equal([a.Id], (await GetAsync<List<TaskDto>>("/api/trash")).Select(t => t.Id));

        // Потом в корзину и сам проект: «Обои» остались своей строкой, «Плинтус» — вместе с проектом
        (await Client.DeleteAsync($"/api/tasks/{repair.Id}")).EnsureSuccessStatusCode();
        Assert.Equal([repair.Id, a.Id], (await GetAsync<List<TaskDto>>("/api/trash")).Select(t => t.Id).Order());

        await PostAsync<TaskDto>($"/api/trash/{a.Id}/restore", new { });
        Assert.Equal(TaskStateDto.Planned, (await TaskAsync(repair.Id)).State);
        var steps = await StepsAsync(repair.Id);
        Assert.Contains(steps, s => s.Id == a.Id);
        Assert.Contains((await GetAsync<List<TaskDto>>("/api/trash")), t => t.Id == b.Id); // остался в корзине
    }

    [Fact]
    public async Task K3_S23_ProjectTrashed_SessionStepsDetachedAndKept_ClipsUnchanged()
    {
        var book = await CreateInDayAsync("Атомные привычки", ThisMonday);
        var reading = await CreateGoalAsync("Ум", "Чтение", ThisMonday);
        var session = await ScheduleAsync(reading, Today);
        await SetDoneAsync(session.Id);
        await SetParentAsync(session.Id, book.Id);
        var before = await HabitTotalAsync(reading.SeriesId);

        (await Client.DeleteAsync($"/api/tasks/{book.Id}")).EnsureSuccessStatusCode();
        var kept = await TaskAsync(session.Id, ThisMonday);
        Assert.Equal((TaskStateDto.Done, (int?)null, reading.Id), (kept.State, kept.ParentId, kept.GoalId!.Value));
        Assert.Equal(before, await HabitTotalAsync(reading.SeriesId));
    }

    // ---------- S28, C: удаление цели ----------

    [Fact]
    public async Task C1_C2_K26_GoalDeletedThisWeekOnly_DoneSessionsStay_UndatedToMonday_StillClips()
    {
        var run = await CreateGoalAsync("Тело", "Бег", ThisMonday);
        var dated = await ScheduleAsync(run, Today);
        await SetDoneAsync(dated.Id);
        // Галочка цели другой недели (здесь — дата «сегодня» не на её неделе) — выполненное занятие без дня
        var nextWeek = (await GetWeekAsync(ThisMonday.AddDays(7))).Goals.Single(g => g.SeriesId == run.SeriesId);
        var toggled = (await PostAsync<GoalResultDto>($"/api/goals/{nextWeek.Id}/toggle", new ToggleGoalRequest(Today))).Task!;
        Assert.Null(toggled.PlannedDate);
        Assert.Equal(2, await HabitTotalAsync(run.SeriesId));

        (await Client.DeleteAsync($"/api/goals/{run.Id}")).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/goals/{nextWeek.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(TaskStateDto.Done, (await TaskAsync(dated.Id, ThisMonday)).State);
        var moved = await TaskAsync(toggled.Id, ThisMonday.AddDays(7));
        Assert.Equal((TaskStateDto.Done, ThisMonday.AddDays(7), (int?)null), (moved.State, moved.PlannedDate!.Value, moved.GoalId));
        Assert.Equal(2, await HabitTotalAsync(run.SeriesId));
    }

    [Fact]
    public async Task C3_K27_KeptClip_MadeSessionOfAnotherGoal_NoLongerClipOfOldSeries()
    {
        var run = await CreateGoalAsync("Тело", "Бег", ThisMonday);
        var session = await ScheduleAsync(run, Today);
        await SetDoneAsync(session.Id);
        (await Client.DeleteAsync($"/api/goals/{run.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(1, await HabitTotalAsync(run.SeriesId));

        var other = await CreateGoalAsync("Тело", "Прогулка", ThisMonday, recurring: false);
        await PutAsync<TaskGoalResultDto>($"/api/tasks/{session.Id}/goal", new SetTaskGoalRequest(other.Id));
        Assert.Equal(0, await HabitTotalAsync(run.SeriesId));
    }

    [Fact]
    public async Task S28_K17_GoalDeleted_UndatedStepSession_StaysStep()
    {
        var book = await CreateInDayAsync("Книга");
        var goal = await CreateGoalAsync("Ум", "Чтение", Monday, recurring: false);
        var session = await ScheduleAsync(goal, Thursday);
        await SetParentAsync(session.Id, book.Id);
        await MoveTaskAsync(session.Id, new StepListTargetDto(book.Id, 0));

        (await Client.DeleteAsync($"/api/goals/{goal.Id}")).EnsureSuccessStatusCode();
        var step = (await StepsAsync(book.Id)).Single();
        Assert.Equal((session.Id, false), (step.Id, step.IsDone));
        Assert.Empty((await GetBoardAsync()).SomedayTasks);
    }

    // ---------- API: что видит клиент ----------

    [Fact]
    public async Task Api_WeekAndBoard_CarryStepsOfVisibleProjects_ParentTitle_DoneReturnsParent()
    {
        var ideas = await CreateCategoryAsync();
        var shed = await CreateTaskAsync("Сарай", new SomedayTargetDto(ideas.Id, 0));
        var boards = await AddStepAsync(shed.Id, "Доски");
        var repair = await CreateInDayAsync("Ремонт");
        var paint = await AddStepAsync(repair.Id, "Покрасить");
        await MoveTaskAsync(boards.Id, new DayTargetDto(Thursday, 0));

        var week = await GetWeekAsync();
        var inDay = week.Tasks.Single(t => t.Id == boards.Id);
        Assert.Equal((shed.Id, "Сарай"), (inDay.ParentId!.Value, inDay.ParentTitle));
        Assert.Equal([paint.Id], week.Steps!.Select(s => s.Id)); // шаги проекта «Ремонт», стоящего в дне
        // Шаги проекта из ленты и (N6.9) открытых проектов плана любой недели
        Assert.Equal([boards.Id, paint.Id], (await GetBoardAsync()).Steps!.Select(s => s.Id).Order());

        var stepDto = (await StepsAsync(shed.Id)).Single();
        Assert.Equal(Thursday, stepDto.PlannedDate);

        var result = await PutAsync<TaskDoneResultDto>($"/api/tasks/{paint.Id}/done", new SetDoneRequest(true));
        Assert.Equal((paint.Id, repair.Id, TaskStateDto.Done), (result.Task.Id, result.Parent!.Id, result.Parent.State));
    }

    [Fact]
    public async Task Api_StepsOrder_GoalProjectTitleAndOpenFlag()
    {
        var repair = await CreateInDayAsync("Ремонт");
        var a = await AddStepAsync(repair.Id, "А");
        var b = await AddStepAsync(repair.Id, "Б");
        (await Client.PutAsJsonAsync($"/api/tasks/{repair.Id}/steps/order", new ReorderRequest([b.Id, a.Id]))).EnsureSuccessStatusCode();
        Assert.Equal(["Б", "А"], (await StepsAsync(repair.Id)).Select(s => s.Title));

        var reading = await SetGoalProjectAsync(await CreateGoalAsync("Ум", "Чтение", Monday, recurring: false), repair.Id);
        Assert.Equal((repair.Id, "Ремонт", true), (reading.ProjectId!.Value, reading.ProjectTitle, reading.ProjectIsOpen));
        await SetDoneAsync(repair.Id);
        var goal = (await GetWeekAsync()).Goals.Single(g => g.Id == reading.Id);
        Assert.False(goal.ProjectIsOpen);
    }

    // ---------- P: проект в шаблоне цели ----------

    [Fact]
    public async Task P2_P4_K24_GoalProject_NewSessionsAreSteps_SpreadsToLaterWeeks_ExistingUntouched()
    {
        var book = await CreateTaskAsync("Атомные привычки", new SomedayTargetDto(null, 0));
        var reading = await CreateGoalAsync("Ум", "Чтение", Monday);
        var before = await ScheduleAsync(reading, Monday);
        reading = await SetGoalProjectAsync(reading, book.Id);

        var first = await ScheduleAsync(reading, Thursday);
        Assert.Equal(book.Id, first.ParentId);
        Assert.Null((await TaskAsync(before.Id)).ParentId); // P4: существующее занятие не меняется

        // Следующая неделя серии — с тем же проектом; «— нет» там — прошлая неделя остаётся с книгой
        var next = (await GetWeekAsync(Monday.AddDays(7))).Goals.Single(g => g.SeriesId == reading.SeriesId);
        Assert.Equal(book.Id, (await ScheduleAsync(next, Monday.AddDays(8))).ParentId);
        next = await SetGoalProjectAsync(next, 0);
        Assert.Null((await ScheduleAsync(next, Monday.AddDays(9))).ParentId);
        Assert.Equal(book.Id, (await ScheduleAsync(reading, Monday.AddDays(5))).ParentId);
    }

    [Fact]
    public async Task P3_K22_ProjectDone_NewSessionsWithoutProject_ReopenedBack()
    {
        var book = await CreateInDayAsync("Книга");
        var reading = await SetGoalProjectAsync(await CreateGoalAsync("Ум", "Чтение", Monday), book.Id);
        await SetDoneAsync(book.Id);
        Assert.Null((await ScheduleAsync(reading, Monday.AddDays(1))).ParentId);

        await SetDoneAsync(book.Id, false);
        Assert.Equal(book.Id, (await ScheduleAsync(reading, Monday.AddDays(2))).ParentId);
    }

    [Fact]
    public async Task P9_SessionOfSameGoal_CannotBeItsProject_StepCannotBeProject()
    {
        var reading = await CreateGoalAsync("Ум", "Чтение", Monday, recurring: false);
        var session = await ScheduleAsync(reading, Monday);
        var repair = await CreateInDayAsync("Ремонт");
        var step = await AddStepAsync(repair.Id, "Обои");

        foreach (var projectId in new[] { session.Id, step.Id })
        {
            var response = await Client.PutAsJsonAsync($"/api/goals/{reading.Id}",
                new UpdateGoalRequest(reading.Title, reading.RoleIds, 1, null, null, null, null, ProjectId: projectId));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
