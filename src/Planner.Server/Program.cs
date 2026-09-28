using Planner.Server.Data;
using Planner.Server.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlannerData();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Место под авторизацию (ASP.NET Identity), когда сервер будет смотреть в сеть:
// builder.Services.AddAuthentication(...);
// builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

// Клиент Blazor WASM (включая _framework) раздаётся этим же сервером
app.MapStaticAssets();

var api = app.MapGroup("/api");
// .RequireAuthorization() — добавить сюда, когда появится Identity
api.MapGet("/ping", () => Results.Ok("pong"));
api.MapBoardEndpoints();
api.MapTaskEndpoints();
api.MapCategoryEndpoints();
api.MapRoleEndpoints();
api.MapGoalEndpoints();
api.MapSettingsEndpoints();

// Неизвестные /api/... — 404, а не index.html
api.MapFallback(() => Results.NotFound());

app.MapFallbackToFile("index.html");

await app.RunAsync();

// Нужен для WebApplicationFactory в тестах
public partial class Program;
