using System.Globalization;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>Ошибка API с текстом для пользователя (ProblemDetails.title) и кодом (ProblemDetails.code).</summary>
public class ApiException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;

    /// <summary>Дело возвращается в ленту без категории — нужно спросить категорию и повторить запрос.</summary>
    public bool IsCategoryRequired => Code == ProblemCodes.CategoryRequired;
}

/// <summary>Типизированный клиент к /api сервера.</summary>
public class PlannerApi(HttpClient http)
{
    // Экран

    public Task<BoardDto> GetBoardAsync() => GetAsync<BoardDto>("api/board");

    public Task<WeekDto> GetWeekAsync(DateOnly weekStart) => GetAsync<WeekDto>($"api/week/{Format(weekStart)}");

    // Дела

    public Task<TaskDto> CreateTaskAsync(string title, TaskTargetDto target) =>
        SendAsync<TaskDto>(HttpMethod.Post, "api/tasks", new CreateTaskRequest(title, target));

    public Task<TaskDto> UpdateTaskAsync(int id, UpdateTaskRequest request) =>
        SendAsync<TaskDto>(HttpMethod.Put, $"api/tasks/{id}", request);

    public Task<TaskDto> MoveTaskAsync(int id, TaskTargetDto target) =>
        SendAsync<TaskDto>(HttpMethod.Post, $"api/tasks/{id}/move", target);

    public Task<TaskDto> SetTaskDoneAsync(int id, bool isDone) =>
        SendAsync<TaskDto>(HttpMethod.Put, $"api/tasks/{id}/done", new SetDoneRequest(isDone));

    public Task TrashTaskAsync(int id) => SendAsync(HttpMethod.Delete, $"api/tasks/{id}");

    // Корзина

    public Task<List<TaskDto>> GetTrashAsync() => GetAsync<List<TaskDto>>("api/trash");

    /// <summary>categoryId — куда вернуть, если своей категории у дела нет (иначе ApiException.IsCategoryRequired).</summary>
    public Task<TaskDto> RestoreTaskAsync(int id, int? categoryId = null) =>
        SendAsync<TaskDto>(HttpMethod.Post, $"api/trash/{id}/restore{CategoryQuery(categoryId)}");

    public Task DeleteForeverAsync(int id) => SendAsync(HttpMethod.Delete, $"api/trash/{id}");

    public Task EmptyTrashAsync() => SendAsync(HttpMethod.Delete, "api/trash");

    // Категории

    public Task<CategoryDto> CreateCategoryAsync(string name, string? emoji) =>
        SendAsync<CategoryDto>(HttpMethod.Post, "api/categories", new CreateCategoryRequest(name, emoji));

    public Task<CategoryDto> UpdateCategoryAsync(int id, UpdateCategoryRequest request) =>
        SendAsync<CategoryDto>(HttpMethod.Put, $"api/categories/{id}", request);

    public Task ReorderCategoriesAsync(IReadOnlyList<int> ids) =>
        SendAsync(HttpMethod.Post, "api/categories/reorder", new ReorderRequest(ids));

    public Task DeleteCategoryAsync(int id) => SendAsync(HttpMethod.Delete, $"api/categories/{id}");

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

    public Task<GoalResultDto> UpdateGoalAsync(int id, UpdateGoalRequest request) =>
        SendAsync<GoalResultDto>(HttpMethod.Put, $"api/goals/{id}", request);

    public Task DeleteGoalAsync(int id) => SendAsync(HttpMethod.Delete, $"api/goals/{id}");

    public Task<WeeklyGoalDto> MoveGoalAsync(int id, MoveGoalRequest request) =>
        SendAsync<WeeklyGoalDto>(HttpMethod.Post, $"api/goals/{id}/move", request);

    public Task<GoalResultDto> ScheduleGoalAsync(int id, TaskTargetDto target) =>
        SendAsync<GoalResultDto>(HttpMethod.Post, $"api/goals/{id}/schedule", target);

    public Task<GoalResultDto> CreateGoalFromTaskAsync(GoalFromTaskRequest request) =>
        SendAsync<GoalResultDto>(HttpMethod.Post, "api/goals/from-task", request);

    public Task<TaskDto> MoveGoalToSomedayAsync(int id, GoalToSomedayRequest request) =>
        SendAsync<TaskDto>(HttpMethod.Post, $"api/goals/{id}/to-someday", request);

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
