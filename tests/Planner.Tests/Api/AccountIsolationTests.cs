using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Storage;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Обязательные проверки защиты по списку ВСЕХ маршрутов (docs/specs/accounts.md, T1, T2): новый эндпоинт без пометки «открыт»
/// или без примера запроса роняет эти тесты.
/// </summary>
public class AccountIsolationTests : ApiTestBase
{
    private const string Marker = "СЕКРЕТ-А";

    /// <summary>Открытые без входа маршруты — только эти (вход, служебное; копии — по ключу скриптов, проверка в фильтре).</summary>
    private static readonly HashSet<string> Anonymous =
    [
        "GET /api/ping", "GET /api/auth/info", "GET /api/auth/me", "POST /api/auth/code", "POST /api/auth/verify",
        "POST /api/auth/invite/check", "POST /api/auth/invite/code", "POST /api/auth/invite/verify",
        "GET /api/backup", "POST /api/backup",
    ];

    private static readonly DateOnly Week = new(2026, 9, 28);

    /// <summary>Все маршруты /api: «МЕТОД /шаблон».</summary>
    private List<(string Key, RouteEndpoint Endpoint)> ApiRoutes() =>
        Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => ($"{m} {e.RoutePattern.RawText!.TrimEnd('/')}", e)))
            .ToList();

    [Fact]
    public void EveryApiRoute_RequiresLogin_UnlessListedAsOpen()
    {
        var routes = ApiRoutes();
        Assert.True(routes.Count > 50, $"маршрутов подозрительно мало: {routes.Count}");

        var wrong = routes.Where(r =>
        {
            var open = r.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var closed = r.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any();
            return open ? !Anonymous.Contains(r.Key) : !closed;
        }).Select(r => r.Key).ToList();

        Assert.True(wrong.Count == 0, "Без входа открыты (или ни открыты, ни закрыты): " + string.Join(", ", wrong));
    }

    [Fact]
    public async Task EveryClosedApiRoute_WithoutLogin_Is401()
    {
        var anonymous = Factory.CreateClient();
        var failures = new List<string>();
        foreach (var (key, _) in ApiRoutes().Where(r => !Anonymous.Contains(r.Key)))
        {
            var (method, url) = Split(key);
            using var request = new HttpRequestMessage(new HttpMethod(method), Fill(url, 1)) { Content = JsonContent.Create(new { }) };
            var status = (await anonymous.SendAsync(request)).StatusCode;
            if (status != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{key} → {(int)status}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(", ", failures));
    }

    [Fact]
    public async Task UserB_OnEveryRoute_NeitherSeesNorChangesDataOfA()
    {
        var a = await CreateDataOfAAsync();
        await Factory.CreateUserAsync("b@test.local");
        var b = await Factory.CreateSignedInClientAsync("b@test.local");
        // У Б свои дела с теми же номерами — запрос с Id из набора А находит своё, а не чужое
        for (var i = 0; i < 8; i++)
        {
            (await b.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"дело Б {i}", new DayTargetDto(Week, 0)))).EnsureSuccessStatusCode();
        }

        Guid adminId;
        await using (var scope = Services.CreateAsyncScope())
        {
            adminId = scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Users
                .Single(u => u.Email == PlannerApiFactory.AdminEmail).Id;
        }

        var fileOfA = Factory.Paths.UserDatabase(adminId);
        var before = await DatabaseFingerprint.ComputeAsync(fileOfA);

        Guid sessionOfA;
        await using (var scope = Services.CreateAsyncScope())
        {
            sessionOfA = scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Sessions.Single(s => s.UserId == adminId).Id;
        }

        var samples = Samples(a, sessionOfA);
        var routes = ApiRoutes().Select(r => r.Key).Distinct().ToList();
        var missing = routes.Where(r => !samples.ContainsKey(r)).ToList();
        Assert.True(missing.Count == 0, "Нет примера запроса для маршрутов (добавьте в Samples): " + string.Join(", ", missing));

        var leaks = new List<string>();
        // Выход — последним: после него Б уже не вошёл
        foreach (var key in routes.OrderBy(k => k.Contains("/logout", StringComparison.Ordinal)))
        {
            var (method, url, body) = samples[key];
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType());
            }

            var response = await b.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (text.Contains(Marker, StringComparison.Ordinal))
            {
                leaks.Add($"{key} ({(int)response.StatusCode})");
            }

            if (key.EndsWith("/api/backup", StringComparison.Ordinal) || key.Contains("/api/admin", StringComparison.Ordinal))
            {
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{key}: {(int)response.StatusCode}"); // не для обычного пользователя
            }
        }

        Assert.True(leaks.Count == 0, "Б увидел данные А: " + string.Join(", ", leaks));
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/board")).StatusCode); // и вход А цел
        var after = await DatabaseFingerprint.ComputeAsync(fileOfA);
        Assert.True(DatabaseFingerprint.Differences(before, after).Count == 0,
            "Б изменил данные А: " + string.Join(", ", DatabaseFingerprint.Differences(before, after)));
    }

    /// <summary>Набор А — все виды данных, в названиях метка.</summary>
    private async Task<Ids> CreateDataOfAAsync()
    {
        var category = await Id(PostAsync<JsonElement>("/api/categories", new CreateCategoryRequest($"Категория {Marker}", null)));
        var thought = await Id(PostAsync<JsonElement>("/api/tasks", new CreateTaskRequest($"Мысль {Marker}", new SomedayTargetDto(category, 0))));
        var task = await Id(PostAsync<JsonElement>("/api/tasks", new CreateTaskRequest($"Дело {Marker}", new DayTargetDto(Week, 0))));
        var project = await Id(PostAsync<JsonElement>("/api/tasks", new CreateTaskRequest($"Проект {Marker}", new SomedayTargetDto(category, 1), IsProject: true)));
        var step = await Id(PostAsync<JsonElement>($"/api/tasks/{project}/steps", new StepRequest($"Шаг {Marker}")));
        var role = await Id(PostAsync<JsonElement>("/api/roles", new CreateRoleRequest($"Роль {Marker}", null)));
        var board = await GetBoardAsync();
        var body = board.Roles.First(r => r.SawDimension is not null).Id;
        var goal = await Id(PostAsync<JsonElement>("/api/goals", new CreateGoalRequest(body, Week, $"Привычка {Marker}", IsRecurring: true)));
        var habits = await GetAsync<JsonElement>("/api/habits");
        var habit = habits.GetProperty("habits").EnumerateArray().First(h => h.GetRawText().Contains(Marker, StringComparison.Ordinal))
            .GetProperty("id").GetInt32();
        var vacation = await Id(PostAsync<JsonElement>("/api/vacations", new CreateVacationRequest(Week, Week.AddDays(6), [habit])));
        await PutAsync<JsonElement>("/api/settings", new UpdateSettingsRequest(Mission: $"Миссия {Marker}"));
        await PutAsync<JsonElement>($"/api/roles/{role}", new UpdateRoleRequest(null, null, $"Кем быть {Marker}"));
        var trashed = await Id(PostAsync<JsonElement>("/api/tasks", new CreateTaskRequest($"В корзине {Marker}", new DayTargetDto(Week, 1))));
        (await Client.DeleteAsync($"/api/tasks/{trashed}")).EnsureSuccessStatusCode();

        return new Ids(category, thought, task, project, step, role, goal, habit, vacation, trashed);
    }

    private static async Task<int> Id(Task<JsonElement> created) => (await created).GetProperty("id").GetInt32();

    private sealed record Ids(int Category, int Thought, int Task, int Project, int Step, int Role, int Goal, int Habit, int Vacation, int Trashed);

    /// <summary>Пример запроса на каждый маршрут — с Id из набора А.</summary>
    private static Dictionary<string, (string Method, string Url, object? Body)> Samples(Ids a, Guid sessionOfA)
    {
        var week = Week.ToString("yyyy-MM-dd");
        var period = "from=2026-09-01&to=2026-10-31";
        TaskTargetDto day = new DayTargetDto(Week, 0);
        var samples = new Dictionary<string, (string, string, object?)>();
        void Add(string method, string pattern, string url, object? body = null) => samples[$"{method} {pattern}"] = (method, url, body);

        Add("GET", "/api/ping", "/api/ping");
        Add("GET", "/api/auth/info", "/api/auth/info");
        Add("GET", "/api/auth/me", "/api/auth/me");
        Add("POST", "/api/auth/code", "/api/auth/code", new RequestCodeRequest(PlannerApiFactory.AdminEmail));
        Add("POST", "/api/auth/verify", "/api/auth/verify", new VerifyCodeRequest(PlannerApiFactory.AdminEmail, "000000"));
        Add("POST", "/api/auth/logout", "/api/auth/logout");
        Add("POST", "/api/auth/logout-all", "/api/auth/logout-all");
        Add("GET", "/api/{*path:nonfile}", "/api/no-such-route");
        Add("POST", "/api/auth/invite/check", "/api/auth/invite/check", new InviteTokenRequest("нет-такого"));
        Add("POST", "/api/auth/invite/code", "/api/auth/invite/code", new InviteCodeRequest("нет-такого", "b@test.local"));
        Add("POST", "/api/auth/invite/verify", "/api/auth/invite/verify", new InviteVerifyRequest("нет-такого", "b@test.local", "000000"));
        Add("GET", "/api/account/sessions", "/api/account/sessions");
        Add("DELETE", "/api/account/sessions/{id:guid}", $"/api/account/sessions/{sessionOfA}");
        Add("GET", "/api/account/export", "/api/account/export");
        Add("POST", "/api/account/delete", "/api/account/delete", new DeleteAccountRequest(PlannerApiFactory.AdminEmail));
        Add("GET", "/api/admin/users", "/api/admin/users");
        Add("PUT", "/api/admin/users/{id:guid}/disabled", $"/api/admin/users/{Guid.NewGuid()}/disabled", new SetUserDisabledRequest(true));
        Add("GET", "/api/admin/invites", "/api/admin/invites");
        Add("POST", "/api/admin/invites", "/api/admin/invites", new CreateInviteRequest(null));
        Add("POST", "/api/admin/invites/{id:int}/revoke", "/api/admin/invites/1/revoke");
        Add("GET", "/api/backup", "/api/backup");
        Add("POST", "/api/backup", "/api/backup");

        Add("GET", "/api/board", "/api/board");
        Add("GET", "/api/week/{weekStart}", $"/api/week/{week}");
        Add("GET", "/api/calendar", $"/api/calendar?{period}");
        Add("GET", "/api/dashboard", $"/api/dashboard?{period}");
        Add("GET", "/api/report", $"/api/report?{period}");
        Add("GET", "/api/report.csv", $"/api/report.csv?{period}");
        Add("GET", "/api/done", "/api/done");
        Add("GET", "/api/search", $"/api/search?q={Uri.EscapeDataString(Marker)}");
        Add("GET", "/api/settings", "/api/settings");
        Add("PUT", "/api/settings", "/api/settings", new UpdateSettingsRequest(Mission: "миссия Б"));
        Add("POST", "/api/thoughts/stale-notices", "/api/thoughts/stale-notices");

        Add("GET", "/api/tasks/{id:int}", $"/api/tasks/{a.Task}");
        Add("POST", "/api/tasks", "/api/tasks", new CreateTaskRequest("новое Б", day));
        Add("POST", "/api/tasks/{id:int}/move", $"/api/tasks/{a.Thought}/move", day);
        Add("PUT", "/api/tasks/{id:int}", $"/api/tasks/{a.Task}", new UpdateTaskRequest("правка Б", null, [], null, null, null));
        Add("PUT", "/api/tasks/{id:int}/done", $"/api/tasks/{a.Task}/done", new SetDoneRequest(true));
        Add("PUT", "/api/tasks/{id:int}/goal", $"/api/tasks/{a.Task}/goal", new SetTaskGoalRequest(a.Goal));
        Add("PUT", "/api/tasks/{id:int}/parent", $"/api/tasks/{a.Task}/parent", new SetParentRequest(a.Project));
        Add("PUT", "/api/tasks/{id:int}/project", $"/api/tasks/{a.Task}/project", new SetProjectRequest(true));
        Add("POST", "/api/tasks/{id:int}/keep", $"/api/tasks/{a.Thought}/keep");
        Add("DELETE", "/api/tasks/{id:int}", $"/api/tasks/{a.Thought}");
        Add("GET", "/api/tasks/{id:int}/steps", $"/api/tasks/{a.Project}/steps");
        Add("POST", "/api/tasks/{id:int}/steps", $"/api/tasks/{a.Project}/steps", new StepRequest("шаг Б"));
        Add("PUT", "/api/tasks/{id:int}/steps/order", $"/api/tasks/{a.Project}/steps/order", new ReorderRequest([a.Step]));
        Add("PUT", "/api/steps/{id:int}", $"/api/steps/{a.Step}", new StepRequest("шаг правка Б"));
        Add("PUT", "/api/steps/{id:int}/done", $"/api/steps/{a.Step}/done", new SetDoneRequest(true));

        Add("GET", "/api/trash", "/api/trash");
        Add("POST", "/api/trash/{id:int}/restore", $"/api/trash/{a.Trashed}/restore");
        Add("DELETE", "/api/trash/{id:int}", $"/api/trash/{a.Trashed}");
        Add("DELETE", "/api/trash", "/api/trash");

        Add("POST", "/api/categories", "/api/categories", new CreateCategoryRequest("категория Б", null));
        Add("POST", "/api/categories/reorder", "/api/categories/reorder", new ReorderRequest([a.Category]));
        Add("PUT", "/api/categories/expanded", "/api/categories/expanded", new SetAllExpandedRequest(false));
        Add("PUT", "/api/categories/{id:int}", $"/api/categories/{a.Category}", new UpdateCategoryRequest("правка Б", null, null));
        Add("DELETE", "/api/categories/{id:int}", $"/api/categories/{a.Category}");

        Add("POST", "/api/roles", "/api/roles", new CreateRoleRequest("роль Б", null));
        Add("POST", "/api/roles/reorder", "/api/roles/reorder", new ReorderRequest([a.Role]));
        Add("PUT", "/api/roles/{id:int}", $"/api/roles/{a.Role}", new UpdateRoleRequest("правка Б", null));
        Add("DELETE", "/api/roles/{id:int}", $"/api/roles/{a.Role}");

        Add("POST", "/api/goals", "/api/goals", new CreateGoalRequest(a.Role, Week, "цель Б"));
        Add("POST", "/api/goals/from-task", "/api/goals/from-task", new GoalFromTaskRequest(a.Task, a.Role, Week, 0));
        Add("PUT", "/api/goals/{id:int}", $"/api/goals/{a.Goal}", new UpdateGoalRequest("правка Б", [a.Role], 1, null, null, null, null));
        Add("POST", "/api/goals/{id:int}/move", $"/api/goals/{a.Goal}/move", new MoveGoalRequest(a.Role, 0));
        Add("POST", "/api/goals/{id:int}/schedule", $"/api/goals/{a.Goal}/schedule", day);
        Add("POST", "/api/goals/{id:int}/steps", $"/api/goals/{a.Goal}/steps", new StepRequest("шаг цели Б"));
        Add("POST", "/api/goals/{id:int}/to-someday", $"/api/goals/{a.Goal}/to-someday", new GoalToSomedayRequest(null, 0));
        Add("POST", "/api/goals/{id:int}/toggle", $"/api/goals/{a.Goal}/toggle", new ToggleGoalRequest(Week));
        Add("GET", "/api/goals/{id:int}/series-end", $"/api/goals/{a.Goal}/series-end");
        Add("DELETE", "/api/goals/{id:int}", $"/api/goals/{a.Goal}");

        Add("GET", "/api/habits", "/api/habits");
        Add("PUT", "/api/habits/{id:int}", $"/api/habits/{a.Habit}", new UpdateHabitRequest(5, 30));
        Add("POST", "/api/habits/{id:int}/plus-one", $"/api/habits/{a.Habit}/plus-one", new PlusOneRequest(Week));
        Add("DELETE", "/api/habits/{id:int}", $"/api/habits/{a.Habit}");
        Add("POST", "/api/vacations", "/api/vacations", new CreateVacationRequest(Week, Week, [a.Habit]));
        Add("DELETE", "/api/vacations/{id:int}", $"/api/vacations/{a.Vacation}");
        return samples;
    }

    private static (string Method, string Url) Split(string key) => (key[..key.IndexOf(' ')], key[(key.IndexOf(' ') + 1)..]);

    /// <summary>Шаблон → адрес: {id:int} → id, {weekStart} → понедельник, {*path} → несуществующий путь.</summary>
    private static string Fill(string pattern, int id) => pattern
        .Replace("{id:int}", id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{weekStart}", Week.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
        .Replace("{id:guid}", Guid.Empty.ToString(), StringComparison.Ordinal)
        .Replace("{*path:nonfile}", "no-such-route", StringComparison.Ordinal);
}
