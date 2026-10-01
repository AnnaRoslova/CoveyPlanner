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
        Assert.Equal(4, board.Roles.Count(r => r.IsSharpenSaw));
        Assert.Equal(["Я сама", "Семья", "Работа", "Дом"], board.Roles.Where(r => !r.IsSharpenSaw).Select(r => r.Name));
        Assert.Equal(18, board.RolePalette.Count);
        Assert.All(board.Roles, r => Assert.Contains(r.Color, board.RolePalette));
        Assert.Empty(board.SomedayTasks);
    }

    [Fact]
    public async Task Unsorted_ThoughtsWithoutCategory_HaveOwnOrder()
    {
        var first = await CreateTaskAsync("Первая", new SomedayTargetDto(null, 0));
        var second = await CreateTaskAsync("Вторая", new SomedayTargetDto(null, 0));

        var unsorted = (await GetBoardAsync()).SomedayTasks.Where(t => t.CategoryId is null).OrderBy(t => t.SortOrder);

        Assert.Equal(["Вторая", "Первая"], unsorted.Select(t => t.Title)); // новая — наверх
        Assert.All([first, second], t => Assert.Equal(TaskStateDto.Someday, t.State));
    }

    [Fact]
    public async Task Unsorted_MoveToCategoryAndBack()
    {
        var ideas = await CreateCategoryAsync("Идеи");
        var thought = await CreateTaskAsync("Мысль", new SomedayTargetDto(null, 0));

        var inCategory = await MoveTaskAsync(thought.Id, new SomedayTargetDto(ideas.Id, 0));
        Assert.Equal(ideas.Id, inCategory.CategoryId);

        var back = await MoveTaskAsync(thought.Id, new SomedayTargetDto(null, 0));
        Assert.Null(back.CategoryId);
        Assert.Equal(TaskStateDto.Someday, back.State);
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
