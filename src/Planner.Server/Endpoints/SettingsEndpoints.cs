using Planner.Core.Services;
using Planner.Server.Data;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Настройки, давно лежащие мысли (docs/specs/batch-ux.md, BS) и быстрый поиск (BK2).</summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api, IWebHostEnvironment environment)
    {
        api.MapGet("/settings", async (SettingsService service, CancellationToken ct) => (await service.GetAsync(ct)).ToDto());

        api.MapPut("/settings", async (UpdateSettingsRequest request, SettingsService service, CancellationToken ct) =>
            (await service.UpdateAsync(request.StaleMonths, request.Mission, request.ShowMissionInPanel, ct)).ToDto());

        // «Оставить» давно лежащую мысль (BS4)
        api.MapPost("/tasks/{id:int}/keep", async (int id, StaleThoughtService service, CancellationToken ct) =>
            (await service.KeepAsync(id, ct)).ToDto());

        // Плашка при загрузке и в полночь (BS4): мысли, перешагнувшие срок с прошлого напоминания; второй раз не придут
        api.MapPost("/thoughts/stale-notices", async (StaleThoughtService service, CancellationToken ct) =>
        {
            var (months, thoughts) = await service.TakeNewlyStaleAsync(ct);
            return new StaleNoticeDto(months, thoughts.Select(t => t.ToDto()).ToList());
        });

        // Быстрый поиск по всем неделям (BK2)
        api.MapGet("/search", async (string? q, SearchService service, TaskViews views, CancellationToken ct) =>
        {
            var result = await service.SearchAsync(q ?? "", ct);
            return new SearchResultDto(
                await views.ToDtosAsync(result.Tasks, ct),
                result.Thoughts.Select(t => t.ToDto()).ToList(),
                await views.ToDtosAsync(result.Projects, ct),
                result.Goals.Select(g => new SearchGoalDto(g.Id, g.Title, g.WeekStart, g.RoleIds)).ToList());
        });

        if (environment.IsDevelopment())
        {
            // Только для e2e: сдвинуть часы сервера (мысли «из прошлого»); 0 — вернуть
            api.MapPost("/dev/clock", (ShiftClockRequest request, ShiftableTimeProvider clock) =>
            {
                clock.Shift = TimeSpan.FromDays(request.Days);
                return Results.NoContent();
            });
        }
    }
}
