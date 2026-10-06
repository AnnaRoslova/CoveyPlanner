using System.Net;
using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Шаги дела: в окне дела, не в днях, ленте и корзине; удаляются вместе с делом.</summary>
public class StepsApiTests : ApiTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private Task<StepDto> AddStepAsync(int taskId, string title) =>
        PostAsync<StepDto>($"/api/tasks/{taskId}/steps", new StepRequest(title));

    [Fact]
    public async Task Steps_CrudOrder_NotInDayFeedOrTrash()
    {
        var task = await CreateInDayAsync("Ремонт", Today);
        var a = await AddStepAsync(task.Id, "Обои");
        var b = await AddStepAsync(task.Id, "Плинтус");
        await PutAsync<StepDto>($"/api/steps/{b.Id}", new StepRequest("Плинтус и пороги"));
        var done = await PutAsync<StepDto>($"/api/steps/{a.Id}/done", new SetDoneRequest(true));
        Assert.True(done.IsDone);

        var steps = await GetAsync<List<StepDto>>($"/api/tasks/{task.Id}/steps");
        Assert.Equal(["Обои", "Плинтус и пороги"], steps.Select(s => s.Title));
        Assert.All(steps, s => Assert.Equal(task.Id, s.ParentId));

        var week = await GetWeekAsync(Today);
        Assert.Equal(["Ремонт"], week.Tasks.Select(t => t.Title));
        Assert.Empty((await GetBoardAsync()).SomedayTasks);

        (await Client.DeleteAsync($"/api/steps/{a.Id}")).EnsureSuccessStatusCode();
        Assert.Single(await GetAsync<List<StepDto>>($"/api/tasks/{task.Id}/steps"));
        Assert.Equal(0, (await GetBoardAsync()).TrashCount);

        // У шага своих шагов нет
        var nested = await Client.PostAsJsonAsync($"/api/tasks/{b.Id}/steps", new StepRequest("x"));
        Assert.Equal(HttpStatusCode.BadRequest, nested.StatusCode);
    }

    [Fact]
    public async Task Steps_DeletedForeverWithTheirTask()
    {
        var task = await CreateInDayAsync("Ремонт", Today);
        await AddStepAsync(task.Id, "Обои");
        (await Client.DeleteAsync($"/api/tasks/{task.Id}")).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/trash/{task.Id}")).EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"/api/tasks/{task.Id}/steps");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
