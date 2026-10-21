using Planner.Shared;

namespace Planner.Client.Services;

// «Моя миссия» (docs/specs/batch-ux.md, BM): текст миссии и «кем я хочу быть» у ролей — на сервере (настройки и роли).
// Сохраняется по уходу из поля, без плашки.
public partial class PlannerState
{
    /// <summary>Текст миссии; null — не написана.</summary>
    public string? Mission { get; private set; }

    /// <summary>Блок «✦ Моя миссия» вверху левой панели (BM2).</summary>
    public bool ShowMissionInPanel { get; private set; }

    public async Task SetMissionAsync(string text)
    {
        var mission = string.IsNullOrWhiteSpace(text) ? null : text;
        if (mission == Mission)
        {
            return;
        }

        Mission = mission;
        NotifyChanged();
        await SaveSettingsAsync(new UpdateSettingsRequest(Mission: text.Trim().Length == 0 ? "" : text));
    }

    public async Task SetShowMissionInPanelAsync(bool show)
    {
        ShowMissionInPanel = show;
        NotifyChanged();
        await SaveSettingsAsync(new UpdateSettingsRequest(ShowMissionInPanel: show));
    }

    /// <summary>«Кем я хочу быть в этой роли»; пусто — стереть.</summary>
    public async Task SetRoleVisionAsync(int roleId, string text)
    {
        var vision = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (FindRole(roleId) is not { } role || role.Vision == vision)
        {
            return;
        }

        ReplaceRole(role with { Vision = vision });
        await SaveAsync(() => api.UpdateRoleAsync(roleId, new UpdateRoleRequest(null, null, vision ?? "")));
    }

    private async Task SaveSettingsAsync(UpdateSettingsRequest request)
    {
        try
        {
            var saved = await api.UpdateSettingsAsync(request);
            Mission = saved.Mission;
            ShowMissionInPanel = saved.ShowMissionInPanel;
        }
        catch (ApiException ex)
        {
            notifier.Error(ex.Message);
        }

        NotifyChanged();
    }
}
