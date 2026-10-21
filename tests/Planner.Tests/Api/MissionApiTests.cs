using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>«Моя миссия» (docs/specs/batch-ux.md, BM): миссия и блок в панели — в настройках, «кем я хочу быть» — у роли.</summary>
public class MissionApiTests : ApiTestBase
{
    [Fact]
    public async Task BM1_Mission_KeepsLineBreaks_PartialUpdateKeepsOtherFields()
    {
        var text = "Жить спокойно и бережно.\nДоводить начатое до конца.";

        await PutAsync<SettingsDto>("/api/settings", new UpdateSettingsRequest(StaleMonths: 6));
        var saved = await PutAsync<SettingsDto>("/api/settings", new UpdateSettingsRequest(Mission: text));
        var shown = await PutAsync<SettingsDto>("/api/settings", new UpdateSettingsRequest(ShowMissionInPanel: true));

        Assert.Equal((6, text), (saved.StaleMonths, saved.Mission));
        Assert.Equal((6, text, true), (shown.StaleMonths, shown.Mission, shown.ShowMissionInPanel));
        Assert.Equal(shown, (await GetBoardAsync()).Settings);
        Assert.Null((await PutAsync<SettingsDto>("/api/settings", new UpdateSettingsRequest(Mission: ""))).Mission);
    }

    [Fact]
    public async Task BM1_MissionTooLong_400()
    {
        var response = await Client.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest(Mission: new string('а', 5001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BM2_RoleVision_SavedAlone_NameAndColorKept_EmptyClears_SawToo()
    {
        var board = await GetBoardAsync();
        var work = board.Roles.Single(r => r.Name == "Работа");
        var body = board.Roles.Single(r => r.Name == "Тело");

        var updated = await PutAsync<RoleDto>($"/api/roles/{work.Id}", new UpdateRoleRequest(null, null, "спокойным профессионалом"));
        var saw = await PutAsync<RoleDto>($"/api/roles/{body.Id}", new UpdateRoleRequest(null, null, "сильной и выносливой"));

        Assert.Equal(("Работа", work.Color, "спокойным профессионалом"), (updated.Name, updated.Color, updated.Vision));
        Assert.Equal("сильной и выносливой", saw.Vision);
        Assert.Equal("спокойным профессионалом", (await GetBoardAsync()).Roles.Single(r => r.Id == work.Id).Vision);
        Assert.Null((await PutAsync<RoleDto>($"/api/roles/{work.Id}", new UpdateRoleRequest(null, null, ""))).Vision);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PutAsJsonAsync($"/api/roles/{work.Id}", new UpdateRoleRequest(null, null, new string('а', 201)))).StatusCode);
    }
}
