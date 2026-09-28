using System.Net;
using Planner.Shared;

namespace Planner.Tests.Api;

public class BoardApiTests : ApiTestBase
{
    [Fact]
    public async Task Board_OnEmptyDatabase_HasRolesButNoCategories()
    {
        var board = await GetBoardAsync();

        Assert.Empty(board.Categories);
        Assert.Null(board.LastUsedCategoryId);
        Assert.Equal(4, board.Roles.Count(r => r.IsSharpenSaw));
        Assert.Equal(["Я сама", "Семья", "Работа", "Дом"], board.Roles.Where(r => !r.IsSharpenSaw).Select(r => r.Name));
        Assert.Equal(10, board.RolePalette.Count);
        Assert.All(board.Roles, r => Assert.Contains(r.Color, board.RolePalette));
        Assert.Empty(board.SomedayTasks);
    }

    [Fact]
    public async Task LastUsedCategory_IsCategoryOfNewestSomedayTask()
    {
        var ideas = await CreateCategoryAsync("Идеи");
        var books = await CreateCategoryAsync("Книги");
        await CreateTaskAsync("Мысль", new SomedayTargetDto(ideas.Id, 0));
        await CreateTaskAsync("Книга", new SomedayTargetDto(books.Id, 0));

        Assert.Equal(books.Id, (await GetBoardAsync()).LastUsedCategoryId);
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
    public async Task SettingsEndpoint_IsGone()
    {
        var response = await Client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
