using Planner.Server.Accounts;
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
builder.Services.AddPlannerAuth();

// Поле "kind" у места назначения может идти в JSON не первым (важно для запросов не из C#)
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddSingleton<IndexHtml>();

var app = builder.Build();

// Перенос на копии без запуска сервера (accounts.md, X3)
if (app.Configuration[TransferCommand.ReportKey] is { Length: > 0 } report)
{
    return await TransferCommand.RunAsync(app.Services, report);
}

// Сверка миграций на копии папки данных (все файлы: accounts.db и каждого пользователя)
if (app.Configuration[TransferCommand.CheckReportKey] is { Length: > 0 } checkReport)
{
    return await TransferCommand.CheckMigrationsAsync(app.Services, checkReport);
}

await app.Services.InitializeStorageAsync();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

// Вход, права, частота, CSRF (docs/specs/accounts.md, S, C)
app.UsePlannerAuth();

// Клиент Blazor WASM (включая _framework) раздаётся этим же сервером
app.MapStaticAssets();

// Все маршруты API — только после входа; открытые помечены AllowAnonymous явно (C; проверка — RouteSecurityTests)
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/ping", () => Results.Ok("pong")).AllowAnonymous();
api.MapAuthEndpoints();
api.MapAdminEndpoints();
api.MapAccountEndpoints();
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

app.MapDevMailbox();

// Страница клиента (с подставленными именами файлов — см. IndexHtml): «/» и любые адреса без расширения
app.MapFallback("{*path:nonfile}", (IndexHtml index, HttpContext context) => index.Serve(context));

await app.RunAsync();
return 0;

// Нужен для WebApplicationFactory в тестах
public partial class Program;
