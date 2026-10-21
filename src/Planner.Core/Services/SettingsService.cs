using Planner.Core.Abstractions;
using Planner.Core.Entities;

namespace Planner.Core.Services;

/// <summary>Настройки приложения на сервере: срок «давно лежат» (batch-ux.md, BS2), миссия и её блок в панели (BM).</summary>
public class SettingsService(ISettingsRepository settings, IUnitOfWork unitOfWork)
{
    public Task<AppSettings> GetAsync(CancellationToken ct = default) => settings.GetAsync(ct);

    /// <summary>null — поле не менять; mission "" — стереть.</summary>
    public async Task<AppSettings> UpdateAsync(int? staleMonths, string? mission, bool? showMissionInPanel, CancellationToken ct = default)
    {
        var current = await settings.GetAsync(ct);
        if (staleMonths is { } months)
        {
            current.SetStaleMonths(months);
        }

        if (mission is not null)
        {
            current.SetMission(mission);
        }

        if (showMissionInPanel is { } show)
        {
            current.SetShowMissionInPanel(show);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return current;
    }
}
