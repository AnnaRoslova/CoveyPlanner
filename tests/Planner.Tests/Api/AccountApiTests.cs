using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planner.Server.Accounts;
using Planner.Server.Data.Accounts;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Свой аккаунт: устройства, выгрузка, удаление с «корзиной» на 7 дней (docs/specs/accounts.md, P1–P3, S4–S5, T3).</summary>
public class AccountApiTests : ApiTestBase
{
    private const string Bee = "bee@test.local";

    private async Task<HttpClient> BeeAsync()
    {
        await Factory.CreateUserAsync(Bee);
        return await Factory.CreateSignedInClientAsync(Bee);
    }

    [Fact]
    public async Task Sessions_ListsDevices_CurrentMarked_RevokeOne()
    {
        ShiftClock(TimeSpan.FromSeconds(61));
        var phone = await Factory.CreateSignedInClientAsync();

        var sessions = await GetAsync<List<SessionDto>>("/api/account/sessions");
        Assert.Equal(2, sessions.Count);
        var mine = Assert.Single(sessions, s => s.IsCurrent);
        var other = Assert.Single(sessions, s => !s.IsCurrent);

        (await Client.DeleteAsync($"/api/account/sessions/{other.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/board")).StatusCode);
        Assert.Equal(mine.Id, Assert.Single(await GetAsync<List<SessionDto>>("/api/account/sessions")).Id);
    }

    [Fact]
    public async Task Sessions_CannotRevokeSomeoneElses()
    {
        var bee = await BeeAsync();
        var adminSession = (await GetAsync<List<SessionDto>>("/api/account/sessions")).Single().Id;

        Assert.Equal(HttpStatusCode.NotFound, (await bee.DeleteAsync($"/api/account/sessions/{adminSession}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/board")).StatusCode);
    }

    [Fact]
    public async Task Export_HasEverythingOfMine_AndNothingOfOthers()
    {
        await CreateCategoryAsync("Моя категория", "💡");
        var bee = await BeeAsync();
        (await bee.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Категория пчелы", null))).EnsureSuccessStatusCode();

        var response = await Client.GetAsync("/api/account/export");
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("coveyplanner-", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("coveyplanner-export", json.GetProperty("format").GetString());
        Assert.Equal(PlannerApiFactory.AdminEmail, json.GetProperty("account").GetProperty("email").GetString());
        var tables = json.GetProperty("tables");
        foreach (var table in new[] { "Tasks", "Categories", "Roles", "WeeklyGoals", "GoalSeries", "Vacations", "AppSettings", "TaskRoles" })
        {
            Assert.True(tables.TryGetProperty(table, out _), $"нет таблицы {table}");
        }

        var text = json.GetRawText();
        Assert.Contains("Моя категория", text);
        Assert.DoesNotContain("Категория пчелы", text);
    }

    [Fact]
    public async Task Delete_WrongEmail_Or_OnlyAdmin_Is400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("x@y.ru"))).StatusCode);
        var admin = await Client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(PlannerApiFactory.AdminEmail));
        Assert.Equal(HttpStatusCode.BadRequest, admin.StatusCode);
        Assert.Contains("единственный администратор", await admin.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Delete_DisablesForSevenDays_LoginByCodeRestores()
    {
        var bee = await BeeAsync();
        (await bee.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Остаётся", null))).EnsureSuccessStatusCode();

        var deleted = await (await bee.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(" BEE@test.local ")))
            .Content.ReadFromJsonAsync<DeletedAccountDto>();
        Assert.InRange(deleted!.DeleteAfter, DateTime.UtcNow.AddDays(7).AddMinutes(-5), DateTime.UtcNow.AddDays(7).AddMinutes(5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await bee.GetAsync("/api/board")).StatusCode);
        Assert.NotNull(Assert.Single(await GetAsync<List<UserAdminDto>>("/api/admin/users"), u => u.Email == Bee).DeleteAfter);

        // Передумала: вход кодом в эти 7 дней
        ShiftClock(TimeSpan.FromDays(6));
        var again = Factory.CreateClient();
        (await again.PostAsJsonAsync("/api/auth/code", new RequestCodeRequest(Bee))).EnsureSuccessStatusCode();
        var me = await (await again.PostAsJsonAsync("/api/auth/verify", new VerifyCodeRequest(Bee, Factory.LastCode(Bee))))
            .Content.ReadFromJsonAsync<MeDto>();
        Assert.True(me!.Restored);
        var board = await again.GetFromJsonAsync<BoardDto>("/api/board");
        Assert.Contains(board!.Categories, c => c.Name == "Остаётся");
        Assert.Null(Assert.Single(await (await Factory.CreateSignedInClientAsync()).GetFromJsonAsync<List<UserAdminDto>>("/api/admin/users")
            ?? [], u => u.Email == Bee).DeleteAfter);
    }

    [Fact]
    public async Task Delete_AfterSevenDays_DataErased()
    {
        var bee = await BeeAsync();
        Guid beeId;
        await using (var scope = Services.CreateAsyncScope())
        {
            beeId = (await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Users.SingleAsync(u => u.Email == Bee)).Id;
        }

        (await bee.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(Bee))).EnsureSuccessStatusCode();
        Assert.True(File.Exists(Factory.Paths.UserDatabase(beeId)));

        ShiftClock(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));
        await using (var scope = Services.CreateAsyncScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AccountPurger>().PurgeDueAsync());
        }

        Assert.False(File.Exists(Factory.Paths.UserDatabase(beeId)));
        await using (var scope = Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.False(await accounts.Users.AnyAsync(u => u.Email == Bee));
            Assert.False(await accounts.Sessions.AnyAsync(s => s.UserId == beeId));
            Assert.False(await accounts.LoginCodes.AnyAsync(c => c.Email == Bee));
        }

        // Кода больше нет: почта не зарегистрирована
        var mailbox = Services.GetRequiredService<DevMailbox>();
        var before = mailbox.Latest(100, Bee).Count;
        (await Factory.CreateClient().PostAsJsonAsync("/api/auth/code", new RequestCodeRequest(Bee))).EnsureSuccessStatusCode();
        Assert.Equal(before, mailbox.Latest(100, Bee).Count);
    }
}
