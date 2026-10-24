using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Planner.Client;
using Planner.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
{
    var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    // Заголовок защиты от подделки запросов: сервер принимает изменения только с ним (docs/specs/accounts.md, C)
    http.DefaultRequestHeaders.Add("X-Planner", "1");
    return http;
});
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<ConnectionQueue>();
builder.Services.AddScoped<PlannerApi>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<LayoutState>();
builder.Services.AddScoped<PlannerState>();
builder.Services.AddScoped<DragDropService>();
builder.Services.AddScoped<TodayWatcher>();
builder.Services.AddScoped<WindowHost>();
builder.Services.AddScoped<ThemeState>();
builder.Services.AddScoped<RoleTasksState>();
builder.Services.AddScoped<ProjectFoldState>();
builder.Services.AddScoped<RightPanelState>();
builder.Services.AddScoped<RoleFoldState>();
builder.Services.AddScoped<TotalsState>();
builder.Services.AddScoped<Hotkeys>();
builder.Services.AddScoped<ReviewReminder>();

await builder.Build().RunAsync();
