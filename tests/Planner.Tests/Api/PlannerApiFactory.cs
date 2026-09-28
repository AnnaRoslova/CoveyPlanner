using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Planner.Server.Data;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Поднимает сервер в памяти с отдельным временным файлом БД.</summary>
public sealed class PlannerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "planner-tests", $"{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(DataServiceCollectionExtensions.DatabasePathKey, _databasePath);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_databasePath)!, Path.GetFileName(_databasePath) + "*"))
        {
            File.Delete(file);
        }
    }
}

/// <summary>База для тестов API: свежий сервер и база на каждый тест + удобные вызовы.</summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    protected static readonly DateOnly Monday = new(2026, 9, 28);

    private readonly PlannerApiFactory _factory = new();

    protected HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

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

    protected async Task<int> InboxIdAsync() => (await GetBoardAsync()).Categories.Single(c => c.IsSystem).Id;

    protected Task<TaskDto> CreateTaskAsync(string title, TaskTargetDto target) =>
        PostAsync<TaskDto>("/api/tasks", new CreateTaskRequest(title, target));

    protected Task<TaskDto> MoveTaskAsync(int id, TaskTargetDto target) =>
        PostTargetAsync<TaskDto>($"/api/tasks/{id}/move", target);

    protected Task<CategoryDto> CreateCategoryAsync(string name) =>
        PostAsync<CategoryDto>("/api/categories", new CreateCategoryRequest(name));
}
