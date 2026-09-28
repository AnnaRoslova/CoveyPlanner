using Planner.Server.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlannerData();

// Место под авторизацию (ASP.NET Identity), когда сервер будет смотреть в сеть:
// builder.Services.AddAuthentication(...);
// builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

// Клиент Blazor WASM (включая _framework) раздаётся этим же сервером
app.MapStaticAssets();

var api = app.MapGroup("/api");
// .RequireAuthorization() — добавить сюда, когда появится Identity
api.MapGet("/ping", () => Results.Ok("pong"));

app.MapFallbackToFile("index.html");

app.Run();

// Нужен для WebApplicationFactory в тестах
public partial class Program;
