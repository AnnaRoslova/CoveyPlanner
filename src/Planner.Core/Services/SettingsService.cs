using Planner.Core.Abstractions;
using Planner.Core.Entities;

namespace Planner.Core.Services;

/// <summary>Настройки приложения на сервере (batch-ux.md, BS2): срок «давно лежат».</summary>
public class SettingsService(ISettingsRepository settings, IUnitOfWork unitOfWork)
{
    public Task<AppSettings> GetAsync(CancellationToken ct = default) => settings.GetAsync(ct);

    public async Task<AppSettings> SetStaleMonthsAsync(int months, CancellationToken ct = default)
    {
        var current = await settings.GetAsync(ct);
        current.SetStaleMonths(months);
        await unitOfWork.SaveChangesAsync(ct);
        return current;
    }
}
