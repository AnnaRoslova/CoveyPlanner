using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Быстрый поиск (docs/specs/batch-ux.md, BK2): группы, все недели, заметки, «ё» = «е», корзина не ищется, до 8 в группе.</summary>
public class SearchApiTests : ApiTestBase
{
    private Task<SearchResultDto> SearchAsync(string q) => GetAsync<SearchResultDto>($"/api/search?q={Uri.EscapeDataString(q)}");

    [Fact]
    public async Task BK2_Groups_AllWeeks_NotesAndYo_NoTrash()
    {
        var far = Monday.AddDays(7 * 10 + 3);
        await CreateInDayAsync("Ёлка на балконе", far);
        await CreateTaskAsync("Купить елку", new SomedayTargetDto(null, 0));
        var project = await CreateTaskAsync("Ремонт балкона", new SomedayTargetDto(null, 0));
        await PutAsync<TaskDto>($"/api/tasks/{project.Id}/project", new SetProjectRequest(true));
        var noted = await CreateInDayAsync("Звонок");
        await PutAsync<TaskDto>($"/api/tasks/{noted.Id}", new UpdateTaskRequest(noted.Title, "про подарок", [], null, null, null));
        var trashed = await CreateTaskAsync("Ёлочные игрушки", new SomedayTargetDto(null, 0));
        (await Client.DeleteAsync($"/api/tasks/{trashed.Id}")).EnsureSuccessStatusCode();
        var roles = (await GetBoardAsync()).Roles;
        await PostAsync<WeeklyGoalDto>("/api/goals", new CreateGoalRequest(roles[0].Id, far.AddDays(-3), "Нарядить ёлку", false));

        var elka = await SearchAsync("ЕЛК");
        var balcony = await SearchAsync("балкон");

        Assert.Equal(["Ёлка на балконе"], elka.Tasks.Select(t => t.Title).ToList());
        Assert.Equal(far, elka.Tasks.Single().PlannedDate);
        Assert.Equal(["Купить елку"], elka.Thoughts.Select(t => t.Title).ToList());
        Assert.Equal(["Нарядить ёлку"], elka.Goals.Select(g => g.Title).ToList());
        Assert.Contains("Звонок", (await SearchAsync("ПОДАРОК")).Tasks.Select(t => t.Title)); // по заметкам
        Assert.Equal(["Ремонт балкона"], balcony.Projects.Select(t => t.Title).ToList());
        Assert.DoesNotContain(elka.Thoughts.Concat(elka.Tasks), t => t.Title == "Ёлочные игрушки");
    }

    [Fact]
    public async Task BK2_EmptyQuery_Nothing_AtMostEightPerGroup()
    {
        for (var i = 0; i < 10; i++)
        {
            await CreateTaskAsync($"Книга {i}", new SomedayTargetDto(null, 0));
        }

        Assert.Equal(8, (await SearchAsync("книга")).Thoughts.Count);
        var empty = await SearchAsync("  ");
        Assert.Empty(empty.Thoughts);
    }
}
