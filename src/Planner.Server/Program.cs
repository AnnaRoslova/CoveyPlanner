using Planner.Server.Data;
using Planner.Server.Data.Storage;
using Planner.Server.Endpoints;
using Planner.Server.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Настройки этой установки вне git (почта администратора, позже SMTP) — settings.json в папке данных (docs/specs/accounts.md, D1);
// переменные окружения и командная строка важнее файла
builder.Configuration
    .AddJsonFile(DataPaths.FromConfiguration(builder.Configuration).SettingsFile, optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddPlannerData();

// Поле "kind" у места назначения может идти в JSON не первым (важно для запросов не из C#)
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddSingleton<IndexHtml>();

// Место под авторизацию (ASP.NET Identity), когда сервер будет смотреть в сеть:
// builder.Services.AddAuthentication(...);
// builder.Services.AddAuthorization();

var app = builder.Build();

// Перенос на копии без запуска сервера (accounts.md, X3)
if (app.Configuration[TransferCommand.ReportKey] is { Length: > 0 } report)
{
    return await TransferCommand.RunAsync(app.Services, report);
}

await app.Services.InitializeStorageAsync();

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
api.MapHabitEndpoints();
api.MapStepEndpoints();
api.MapDoneEndpoints();
api.MapReportEndpoints();
api.MapBackupEndpoints();
api.MapSettingsEndpoints(app.Environment);

// Неизвестные /api/... — 404, а не index.html
api.MapFallback(() => Results.NotFound());

// Страница клиента (с подставленными именами файлов — см. IndexHtml): «/» и любые адреса без расширения
app.MapFallback("{*path:nonfile}", (IndexHtml index, HttpContext context) => index.Serve(context));

await app.RunAsync();
return 0;

// Нужен для WebApplicationFactory в тестах
public partial class Program;
