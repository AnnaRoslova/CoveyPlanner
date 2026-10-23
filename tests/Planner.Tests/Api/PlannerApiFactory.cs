using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Planner.Server.Data;
using Planner.Server.Data.Storage;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Поднимает сервер в памяти со своей временной папкой данных (accounts.db, users*.db, ключи) и своей папкой резервных копий.
/// Администратор — <see cref="AdminEmail"/> (adminEmail = null — не задан). Ежедневная копия при запуске выключена (кроме тестов, которые включают её сами).
/// clock — часы сервера вместо настоящих (тест не зависит от того, перешли ли сутки за время теста; batch-3.md, FT1).
/// </summary>
public sealed class PlannerApiFactory(
    string? dataDirectory = null, bool dailyBackup = false, string? backupDirectory = null, TimeProvider? clock = null,
    string? adminEmail = PlannerApiFactory.AdminEmail)
    : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";

    public string DataDirectory { get; } =
        dataDirectory ?? Path.Combine(Path.GetTempPath(), "planner-tests", $"data-{Guid.NewGuid():N}");

    public string BackupDirectory { get; } =
        backupDirectory ?? Path.Combine(Path.GetTempPath(), "planner-tests", $"backups-{Guid.NewGuid():N}");

    public DataPaths Paths => new(DataDirectory);

    /// <summary>Клиент служебных скриптов — с ключом из local.key (accounts.md, D6).</summary>
    public HttpClient CreateScriptClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(LocalKey.Header, File.ReadAllText(Paths.LocalKeyFile).Trim());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(DataPaths.DataDirectoryKey, DataDirectory);
        builder.UseSetting(DataServiceCollectionExtensions.BackupPathKey, BackupDirectory);
        builder.UseSetting(StorageInitializer.DailyBackupKey, dailyBackup.ToString());
        if (adminEmail is not null)
        {
            builder.UseSetting(StorageInitializer.AdminEmailKey, adminEmail);
        }

        if (clock is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        // Папки, переданные тестом, — его забота (их может использовать следующий запуск сервера)
        if (dataDirectory is null)
        {
            TestDatabase.DeleteDirectory(DataDirectory);
        }

        if (backupDirectory is null)
        {
            TestDatabase.DeleteDirectory(BackupDirectory);
        }
    }
}

/// <summary>База для тестов API: свежий сервер и база на каждый тест + удобные вызовы.</summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    protected static readonly DateOnly Monday = new(2026, 9, 28);

    private readonly PlannerApiFactory _factory = new();

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>Сервисы сервера — посмотреть базу напрямую (то, чего не видно через API).</summary>
    protected IServiceProvider Services => _factory.Services;

    public Task InitializeAsync()
    {
        Client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Сдвинуть часы сервера (мысли «из прошлого», batch-ux.md BS); TimeSpan.Zero — вернуть.</summary>
    protected void ShiftClock(TimeSpan shift) => _factory.Services.GetRequiredService<ShiftableTimeProvider>().Shift = shift;

    protected async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"{(int)response.StatusCode}: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    protected async Task<T> GetAsync<T>(string url) => await ReadAsync<T>(await Client.GetAsync(url));

    protected async Task<T> PostAsync<T>(string url, object body) =>
        await ReadAsync<T>(await Client.PostAsJsonAsync(url, body));

    /// <summary>Место назначения нужно слать именно как TaskTargetDto — иначе в JSON не будет поля "kind".</summary>
    protected async Task<T> PostTargetAsync<T>(string url, TaskTargetDto target) =>
        await ReadAsync<T>(await Client.PostAsJsonAsync(url, target));

    protected async Task<T> PutAsync<T>(string url, object body) =>
        await ReadAsync<T>(await Client.PutAsJsonAsync(url, body));

    protected Task<BoardDto> GetBoardAsync() => GetAsync<BoardDto>("/api/board");

    protected Task<WeekDto> GetWeekAsync(DateOnly? weekStart = null) =>
        GetAsync<WeekDto>($"/api/week/{weekStart ?? Monday:yyyy-MM-dd}");

    protected Task<TaskDto> CreateTaskAsync(string title, TaskTargetDto target) =>
        PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest(title, target));

    protected Task<TaskDto> CreateInDayAsync(string title, DateOnly? date = null, int index = 0) =>
        CreateTaskAsync(title, new DayTargetDto(date ?? Monday, index));

    protected Task<TaskDto> MoveTaskAsync(int id, TaskTargetDto target) =>
        PostTargetAsync<TaskDto>($"/api/tasks/{id}/move", target);

    protected Task<TaskDto> UpdateTaskAsync(TaskDto task, TimeOnly? startTime = null, IReadOnlyList<int>? roleIds = null) =>
        PutAsync<TaskDto>($"/api/tasks/{task.Id}",
            new UpdateTaskRequest(task.Title, task.Notes, roleIds ?? task.RoleIds, task.Quadrant, startTime, task.DurationMinutes));

    /// <summary>Отметить дело (или снять отметку); родитель, отметившийся сам, — в ответе /done, здесь не нужен.</summary>
    protected async Task<TaskDto> SetTaskDoneAsync(int id, bool isDone) =>
        (await PutAsync<TaskDoneResultDto>($"/api/tasks/{id}/done", new SetDoneRequest(isDone))).Task;

    protected Task<CategoryDto> CreateCategoryAsync(string name = "Идеи", string? emoji = null) =>
        PostAsync<CategoryDto>("/api/categories", new CreateCategoryRequest(name, emoji));
}
