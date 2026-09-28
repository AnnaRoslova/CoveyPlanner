using Planner.Core.Abstractions;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Настройки сетки часов.</summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (ISettingsRepository settings, CancellationToken ct) =>
            (await settings.GetAsync(ct)).ToDto());

        api.MapPut("/settings", async (SettingsDto request, ISettingsRepository settings, IUnitOfWork unitOfWork, CancellationToken ct) =>
        {
            var current = await settings.GetAsync(ct);
            current.Update(request.DayStartHour, request.DayEndHour, request.SlotMinutes);
            await unitOfWork.SaveChangesAsync(ct);
            return current.ToDto();
        });
    }
}
