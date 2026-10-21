using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>
/// Давно лежащие мысли (docs/specs/batch-ux.md, BS): возраст — от последнего изменения по существу (порядок не считается,
/// другая категория — да), срок из настроек, плашка — один раз за перешагивание, «Оставить» — свежая на один срок.
/// </summary>
public class StaleThoughtsApiTests : ApiTestBase
{
    private static readonly TimeSpan FourMonthsAgo = -TimeSpan.FromDays(122);

    private async Task<TaskDto> OldThoughtAsync(string title, int? categoryId = null, TimeSpan? age = null)
    {
        ShiftClock(age ?? FourMonthsAgo);
        var thought = await CreateTaskAsync(title, new SomedayTargetDto(categoryId, 0));
        ShiftClock(TimeSpan.Zero);
        return thought;
    }

    private async Task<int> StaleCountAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return (await GetAsync<DashboardDto>($"/api/dashboard?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).Stale.Count;
    }

    private Task<StaleNoticeDto> NoticesAsync() => PostAsync<StaleNoticeDto>("/api/thoughts/stale-notices", new { });

    [Fact]
    public async Task BS4_NoticeOncePerCrossing_KeepMakesFresh()
    {
        var garden = await OldThoughtAsync("Сад на балконе");
        await CreateTaskAsync("Свежая", new SomedayTargetDto(null, 0));

        var first = await NoticesAsync();
        var second = await NoticesAsync();

        Assert.Equal((3, "Сад на балконе"), (first.Months, first.Thoughts.Single().Title));
        Assert.Empty(second.Thoughts);
        Assert.Equal(1, await StaleCountAsync());

        var kept = await PostAsync<TaskDto>($"/api/tasks/{garden.Id}/keep", new { });

        Assert.NotNull(kept.KeptAt);
        Assert.Equal(0, await StaleCountAsync());
        Assert.Empty((await NoticesAsync()).Thoughts);
    }

    [Fact]
    public async Task BS1_RenameRefreshes_ReorderDoesNot_OtherCategoryRefreshes()
    {
        var ideas = await CreateCategoryAsync("Идеи");
        var books = await CreateCategoryAsync("Книги");
        var renamed = await OldThoughtAsync("Сад", ideas.Id);
        var reordered = await OldThoughtAsync("Дача", ideas.Id);
        var moved = await OldThoughtAsync("Дюна", ideas.Id);
        Assert.Equal(3, await StaleCountAsync());

        await PutAsync<TaskDto>($"/api/tasks/{renamed.Id}", new UpdateTaskRequest("Сад на балконе", null, [], null, null, null));
        await MoveTaskAsync(reordered.Id, new SomedayTargetDto(ideas.Id, 2));
        var after = await MoveTaskAsync(moved.Id, new SomedayTargetDto(books.Id, 0));

        Assert.Equal(1, await StaleCountAsync()); // осталась только «Дача» — её лишь переставили
        Assert.True(after.UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task BS2_TermFromSettings_1To36Months()
    {
        await OldThoughtAsync("Два месяца", age: -TimeSpan.FromDays(62));
        Assert.Equal(0, await StaleCountAsync());

        var settings = await PutAsync<SettingsDto>("/api/settings", new UpdateSettingsRequest(1));

        Assert.Equal(1, settings.StaleMonths);
        Assert.Equal(1, (await GetAsync<SettingsDto>("/api/settings")).StaleMonths);
        Assert.Equal(1, (await GetBoardAsync()).StaleMonths);
        Assert.Equal(1, await StaleCountAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest(37))).StatusCode);
    }

    [Fact]
    public async Task BS5_ProjectsAndPlannedAreNotStale_KeepOnlyThoughts()
    {
        ShiftClock(FourMonthsAgo);
        var project = await CreateTaskAsync("Ремонт", new SomedayTargetDto(null, 0));
        await PutAsync<TaskDto>($"/api/tasks/{project.Id}/project", new SetProjectRequest(true));
        var planned = await CreateInDayAsync("Звонок", DateOnly.FromDateTime(DateTime.Now));
        ShiftClock(TimeSpan.Zero);

        Assert.Equal(0, await StaleCountAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync($"/api/tasks/{planned.Id}/keep", new { })).StatusCode);
    }
}
