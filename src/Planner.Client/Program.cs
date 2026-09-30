using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Planner.Client;
using Planner.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<PlannerApi>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<LayoutState>();
builder.Services.AddScoped<PlannerState>();
builder.Services.AddScoped<DragDropService>();
builder.Services.AddScoped<TodayWatcher>();
builder.Services.AddScoped<GoalEditorHost>();
builder.Services.AddScoped<ThemeState>();

await builder.Build().RunAsync();
