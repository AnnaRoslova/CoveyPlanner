using System.Globalization;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>Ошибка API с текстом для пользователя (ProblemDetails.title) и кодом (ProblemDetails.code).</summary>
public class ApiException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;

    /// <summary>Дело возвращается в ленту без категории — нужно спросить категорию и повторить запрос.</summary>
}

/// <summary>Типизированный клиент к /api сервера.</summary>
public class PlannerApi(HttpClient http)
{
    // Экран

    public Task<BoardDto> GetBoardAsync() => GetAsync<BoardDto>("api/board");

    public Task<WeekDto> GetWeekAsync(DateOnly weekStart) => GetAsync<WeekDto>($"api/week/{Format(weekStart)}");

    // Дела

    public Task<TaskDto> CreateTaskAsync(string title, TaskTargetDto target, bool isProject = false) =>
        SendAsync<TaskDto>(HttpMethod.Post, "api/tasks", new CreateTaskRequest(title, target, isProject));

    /// <summary>«🛠 Сделать проектом» / «🛠 больше не проект» (N6).</summary>
    public Task<TaskDto> SetProjectAsync(int id, bool isProject) =>
        SendAsync<TaskDto>(HttpMethod.Put, $"api/tasks/{id}/project", new SetProjectRequest(isProject));


    public Task<TaskDto> UpdateTaskAsync(int id, UpdateTaskRequest request) =>
        SendAsync<TaskDto>(HttpMethod.Put, $"api/tasks/{id}", request);

    public Task<TaskDto> MoveTaskAsync(int id, TaskTargetDto target) =>
        SendAsync<TaskDto>(HttpMethod.Post, $"api/tasks/{id}/move", target);

    /// <summary>Занятием какой цели сделать дело (null — отвязать); в ответе — затронутые цели и заменённые занятия.</summary>
    public Task<TaskGoalResultDto> SetTaskGoalAsync(int id, int? goalId) =>
        SendAsync<TaskGoalResultDto>(HttpMethod.Put, $"api/tasks/{id}/goal", new SetTaskGoalRequest(goalId));

    /// <summary>Отметка; Parent — родитель, если он отметился сам (все шаги сделаны).</summary>
    public Task<TaskDoneResultDto> SetTaskDoneAsync(int id, bool isDone, int? viaSessionId = null) =>
        SendAsync<TaskDoneResultDto>(HttpMethod.Put, $"api/tasks/{id}/done", new SetDoneRequest(isDone, viaSessionId));

    public Task TrashTaskAsync(int id) => SendAsync(HttpMethod.Delete, $"api/tasks/{id}");

    // Корзина

    public Task<List<TaskDto>> GetTrashAsync() => GetAsync<List<TaskDto>>("api/trash");

    /// <summary>Одно дело — например, проект шага, стоящий на другой неделе.</summary>
    public Task<TaskDto> GetTaskAsync(int id) => GetAsync<TaskDto>($"api/tasks/{id}");

    /// <summary>«+ шаг» цели: в её проект (нет проекта — он создаётся в «неразобранном», G3).</summary>
    public Task<GoalStepResultDto> AddGoalStepAsync(int goalId, string title) =>
        SendAsync<GoalStepResultDto>(HttpMethod.Post, $"api/goals/{goalId}/steps", new StepRequest(title));

    /// <summary>«Проект» в карточке: сделать дело шагом parentId (null — убрать из проекта).</summary>
    public Task<TaskDto> SetParentAsync(int id, int? parentId) =>
        SendAsync<TaskDto>(HttpMethod.Put, $"api/tasks/{id}/parent", new SetParentRequest(parentId));

    /// <summary>Дело ленты без своей категории вернётся в «неразобранное».</summary>
    public Task<TaskDto> RestoreTaskAsync(int id) =>
        SendAsync<TaskDto>(HttpMethod.Post, $"api/trash/{id}/restore");

    public Task DeleteForeverAsync(int id) => SendAsync(HttpMethod.Delete, $"api/trash/{id}");

    public Task EmptyTrashAsync() => SendAsync(HttpMethod.Delete, "api/trash");

    // Категории

    public Task<CategoryDto> CreateCategoryAsync(string name, string? emoji, bool atTop) =>
        SendAsync<CategoryDto>(HttpMethod.Post, "api/categories", new CreateCategoryRequest(name, emoji, atTop));

    public Task SetAllCategoriesExpandedAsync(bool isExpanded) =>
        SendAsync(HttpMethod.Put, "api/categories/expanded", new SetAllExpandedRequest(isExpanded));

    public Task<CategoryDto> UpdateCategoryAsync(int id, UpdateCategoryRequest request) =>
        SendAsync<CategoryDto>(HttpMethod.Put, $"api/categories/{id}", request);

    public Task ReorderCategoriesAsync(IReadOnlyList<int> ids) =>
        SendAsync(HttpMethod.Post, "api/categories/reorder", new ReorderRequest(ids));

    /// <summary>keepTasks — дела категории переложить в «неразобранное», иначе — в корзину.</summary>
    public Task DeleteCategoryAsync(int id, bool keepTasks) =>
        SendAsync(HttpMethod.Delete, $"api/categories/{id}?keepTasks={(keepTasks ? "true" : "false")}");

    // Роли

    public Task<RoleDto> CreateRoleAsync(string name, string? color) =>
        SendAsync<RoleDto>(HttpMethod.Post, "api/roles", new CreateRoleRequest(name, color));

    public Task<RoleDto> UpdateRoleAsync(int id, UpdateRoleRequest request) =>
        SendAsync<RoleDto>(HttpMethod.Put, $"api/roles/{id}", request);

    public Task ReorderRolesAsync(IReadOnlyList<int> ids) =>
        SendAsync(HttpMethod.Post, "api/roles/reorder", new ReorderRequest(ids));

    public Task DeleteRoleAsync(int id) => SendAsync(HttpMethod.Delete, $"api/roles/{id}");

    // Цели

    public Task<WeeklyGoalDto> CreateGoalAsync(CreateGoalRequest request) =>
        SendAsync<WeeklyGoalDto>(HttpMethod.Post, "api/goals", request);

    public Task<GoalUpdateResultDto> UpdateGoalAsync(int id, UpdateGoalRequest request) =>
        SendAsync<GoalUpdateResultDto>(HttpMethod.Put, $"api/goals/{id}", request);

    public Task<SeriesEndPreviewDto> GetSeriesEndPreviewAsync(int id) => GetAsync<SeriesEndPreviewDto>($"api/goals/{id}/series-end");

    public Task<GoalResultDto> ToggleGoalAsync(int id, ToggleGoalRequest request) =>
        SendAsync<GoalResultDto>(HttpMethod.Post, $"api/goals/{id}/toggle", request);

    /// <summary>Постоянная цель: fromThisWeekOn — с этой недели и дальше, иначе — только на этой неделе.</summary>
    public Task DeleteGoalAsync(int id, bool fromThisWeekOn) =>
        SendAsync(HttpMethod.Delete, $"api/goals/{id}{(fromThisWeekOn ? "?scope=series" : "")}");

    public Task<WeeklyGoalDto> MoveGoalAsync(int id, MoveGoalRequest request) =>
        SendAsync<WeeklyGoalDto>(HttpMethod.Post, $"api/goals/{id}/move", request);

    public Task<GoalResultDto> ScheduleGoalAsync(int id, TaskTargetDto target) =>
        SendAsync<GoalResultDto>(HttpMethod.Post, $"api/goals/{id}/schedule", target);

    public Task<GoalResultDto> CreateGoalFromTaskAsync(GoalFromTaskRequest request) =>
        SendAsync<GoalResultDto>(HttpMethod.Post, "api/goals/from-task", request);

    public Task<TaskDto> MoveGoalToSomedayAsync(int id, GoalToSomedayRequest request) =>
        SendAsync<TaskDto>(HttpMethod.Post, $"api/goals/{id}/to-someday", request);

    // «Выполненное»

    /// <summary>Выполненные дела из ленты по категориям, свежие сверху (только чтение).</summary>
    public Task<List<DoneGroupDto>> GetDoneAsync() => GetAsync<List<DoneGroupDto>>("api/done");

    public Task<SettingsDto> SetSettingsAsync(int staleMonths) => SendAsync<SettingsDto>(HttpMethod.Put, "api/settings", new UpdateSettingsRequest(staleMonths));

    public Task<TaskDto> KeepTaskAsync(int id) => SendAsync<TaskDto>(HttpMethod.Post, $"api/tasks/{id}/keep");

    public Task<StaleNoticeDto> TakeStaleNoticesAsync() => SendAsync<StaleNoticeDto>(HttpMethod.Post, "api/thoughts/stale-notices");

    public Task<SearchResultDto> SearchAsync(string query) => GetAsync<SearchResultDto>($"api/search?q={Uri.EscapeDataString(query)}");

    public Task<DashboardDto> GetDashboardAsync(DateOnly from, DateOnly to) =>
        GetAsync<DashboardDto>($"api/dashboard?from={Format(from)}&to={Format(to)}");

    public Task<ReportDto> GetReportAsync(DateOnly from, DateOnly to) =>
        GetAsync<ReportDto>($"api/report?from={Format(from)}&to={Format(to)}");

    /// <summary>Адрес CSV отчёта — для ссылки «Скачать CSV» (файл отдаёт сервер).</summary>
    public static string ReportCsvUrl(DateOnly from, DateOnly to) => $"api/report.csv?from={Format(from)}&to={Format(to)}";

    // Шаги дела

    public Task<StepDto> AddStepAsync(int taskId, string title) =>
        SendAsync<StepDto>(HttpMethod.Post, $"api/tasks/{taskId}/steps", new StepRequest(title));

    // Привычки (постоянные цели пилы) и каникулы

    /// <summary>Все привычки со статистикой на день <paramref name="today"/> и каникулы. Только чтение — недели не создаются.</summary>
    public Task<HabitsDto> GetHabitsAsync(DateOnly today) => GetAsync<HabitsDto>($"api/habits?today={Format(today)}");

    public Task<HabitDto> UpdateHabitAsync(int id, UpdateHabitRequest request, DateOnly today) =>
        SendAsync<HabitDto>(HttpMethod.Put, $"api/habits/{id}?today={Format(today)}", request);

    public Task DeleteHabitAsync(int id) => SendAsync(HttpMethod.Delete, $"api/habits/{id}");

    /// <summary>«+1 сегодня»: отмеченное занятие цели привычки на сегодня.</summary>
    public Task<PlusOneResultDto> PlusOneAsync(int id, DateOnly today) =>
        SendAsync<PlusOneResultDto>(HttpMethod.Post, $"api/habits/{id}/plus-one", new PlusOneRequest(today));

    public Task<VacationDto> CreateVacationAsync(CreateVacationRequest request) =>
        SendAsync<VacationDto>(HttpMethod.Post, "api/vacations", request);

    public Task DeleteVacationAsync(int id) => SendAsync(HttpMethod.Delete, $"api/vacations/{id}");

    private static string CategoryQuery(int? categoryId) => categoryId is { } id ? $"?categoryId={id}" : "";

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<T> GetAsync<T>(string url) => await SendAsync<T>(HttpMethod.Get, url);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendRawAsync(method, url, body);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendRawAsync(method, url, body);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, object? body)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            // Тип берём объявленный (например, TaskTargetDto), чтобы в JSON попало поле "kind"
            request.Content = body is TaskTargetDto target
                ? JsonContent.Create(target)
                : JsonContent.Create(body, body.GetType());
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Нет связи с сервером.");
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var problem = await TryReadProblemAsync(response);
        response.Dispose();
        throw new ApiException(problem?.Title ?? $"Ошибка сервера ({(int)response.StatusCode}).", problem?.Code);
    }

    private static async Task<Problem?> TryReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Нужные поля ProblemDetails.</summary>
    private sealed record Problem(string? Title, string? Code);
}
