using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

public class BoardApiTests : ApiTestBase
{
    [Fact]
    public async Task Board_OnEmptyDatabase_HasSeedData()
    {
        var board = await GetBoardAsync();

        Assert.Equal(new SettingsDto(7, 23, 30), board.Settings);
        Assert.Single(board.Categories, c => c.IsSystem && c.Name == "Входящие");
        Assert.Equal(4, board.Roles.Count(r => r.IsSharpenSaw));
        Assert.Equal(["Я сама", "Семья", "Работа", "Дом"], board.Roles.Where(r => !r.IsSharpenSaw).Select(r => r.Name));
        Assert.Equal(10, board.RolePalette.Count);
        Assert.Empty(board.SomedayTasks);
    }

    [Fact]
    public async Task Week_AnyDate_NormalizedToMonday()
    {
        var week = await GetWeekAsync(Monday.AddDays(3));

        Assert.Equal(Monday, week.WeekStart);
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404_NotIndexHtml()
    {
        var response = await Client.GetAsync("/api/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Settings_UpdateAndValidate()
    {
        var updated = await PutAsync<SettingsDto>("/api/settings", new SettingsDto(6, 22, 15));
        Assert.Equal(new SettingsDto(6, 22, 15), updated);

        var bad = await Client.PutAsJsonAsync("/api/settings", new SettingsDto(10, 9, 30));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
