using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planner.Server.Accounts;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Приглашения, отключение пользователей, стартовый набор, режим Open (docs/specs/accounts.md, I1–I7, U1–U4, N1, Q5, T3).</summary>
public class InviteApiTests : ApiTestBase
{
    private HttpClient Anonymous() => Factory.CreateClient();

    private static string TokenOf(CreatedInviteDto created) => created.Link[(created.Link.IndexOf('#') + 1)..];

    private Task<CreatedInviteDto> CreateInviteAsync(string? email = null, int days = 7) =>
        PostAsync<CreatedInviteDto>("/api/admin/invites", new CreateInviteRequest(email, days));

    private async Task<List<InviteDto>> InvitesAsync() => await GetAsync<List<InviteDto>>("/api/admin/invites");

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;

    /// <summary>Регистрация по ссылке: код на почту → код из письма.</summary>
    private async Task<(HttpClient Client, HttpResponseMessage Response)> RegisterAsync(string token, string email)
    {
        var client = Anonymous();
        (await client.PostAsJsonAsync("/api/auth/invite/code", new InviteCodeRequest(token, email))).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/auth/invite/verify", new InviteVerifyRequest(token, email, Factory.LastCode(email)));
        return (client, response);
    }

    [Fact]
    public async Task Invite_WithoutEmail_RegistersAnyNewAddress_WithStarterSet_ThenIsUsed()
    {
        var created = await CreateInviteAsync();
        Assert.Contains("/invite#", created.Link);
        Assert.Equal("Active", created.Invite.Status);
        Assert.Equal(new InviteCheckDto(true, null),
            await (await Anonymous().PostAsJsonAsync("/api/auth/invite/check", new InviteTokenRequest(TokenOf(created)))).Content.ReadFromJsonAsync<InviteCheckDto>());

        var (client, response) = await RegisterAsync(TokenOf(created), "Olga@Example.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("olga@example.com", me!.Email);
        Assert.False(me.IsAdmin);

        // Стартовый набор (N1): 4 измерения пилы, «Я», «Семья», «Работа», «Дом»; «💡 Идеи», «📚 Почитать»
        var board = await client.GetFromJsonAsync<BoardDto>("/api/board");
        Assert.Equal(["Тело", "Ум", "Дух", "Общение и эмоции", "Я", "Семья", "Работа", "Дом"], board!.Roles.Select(r => r.Name));
        Assert.Equal(["Идеи", "Почитать"], board.Categories.Select(c => c.Name));
        Assert.Equal(["💡", "📚"], board.Categories.Select(c => c.Emoji));

        var used = Assert.Single(await InvitesAsync());
        Assert.Equal("Used", used.Status);
        Assert.Equal("olga@example.com", used.UsedByEmail);

        // Второй раз ссылка не действует
        var again = await Anonymous().PostAsJsonAsync("/api/auth/invite/code", new InviteCodeRequest(TokenOf(created), "other@example.com"));
        Assert.Equal("invite-invalid", await CodeOf(again));
    }

    [Fact]
    public async Task Invite_ForEmail_SendsLetter_AcceptsOnlyThatEmail()
    {
        var created = await CreateInviteAsync("bound@example.com");
        var letter = Services.GetRequiredService<DevMailbox>().Latest(1, "bound@example.com").Single();
        Assert.Contains(created.Link, letter.Body);

        var check = await (await Anonymous().PostAsJsonAsync("/api/auth/invite/check", new InviteTokenRequest(TokenOf(created))))
            .Content.ReadFromJsonAsync<InviteCheckDto>();
        Assert.Equal("bound@example.com", check!.Email);

        var other = await Anonymous().PostAsJsonAsync("/api/auth/invite/code", new InviteCodeRequest(TokenOf(created), "other@example.com"));
        Assert.Equal("invite-other-email", await CodeOf(other));

        var (_, response) = await RegisterAsync(TokenOf(created), "bound@example.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invite_Expired_Revoked_Unknown_AllSayNotValid()
    {
        var expiring = await CreateInviteAsync(days: 1);
        var revoked = await CreateInviteAsync();
        (await Client.PostAsync($"/api/admin/invites/{revoked.Invite.Id}/revoke", null)).EnsureSuccessStatusCode();
        ShiftClock(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));

        foreach (var token in new[] { TokenOf(expiring), TokenOf(revoked), "нет-такого" })
        {
            var check = await (await Anonymous().PostAsJsonAsync("/api/auth/invite/check", new InviteTokenRequest(token)))
                .Content.ReadFromJsonAsync<InviteCheckDto>();
            Assert.False(check!.Valid);
        }

        var statuses = (await InvitesAsync()).ToDictionary(i => i.Id, i => i.Status);
        Assert.Equal("Expired", statuses[expiring.Invite.Id]);
        Assert.Equal("Revoked", statuses[revoked.Invite.Id]);
    }

    [Fact]
    public async Task Invite_RegisteredEmail_SaysSo_InviteNotSpent()
    {
        var created = await CreateInviteAsync();
        var response = await Anonymous().PostAsJsonAsync("/api/auth/invite/code", new InviteCodeRequest(TokenOf(created), PlannerApiFactory.AdminEmail));

        Assert.Equal("registered", await CodeOf(response));
        Assert.Equal("Active", Assert.Single(await InvitesAsync()).Status);
    }

    [Fact]
    public async Task Invite_WrongCode_DoesNotSpendInvite()
    {
        var created = await CreateInviteAsync();
        var client = Anonymous();
        (await client.PostAsJsonAsync("/api/auth/invite/code", new InviteCodeRequest(TokenOf(created), "new@example.com"))).EnsureSuccessStatusCode();
        var code = Factory.LastCode("new@example.com");
        var wrong = await client.PostAsJsonAsync("/api/auth/invite/verify",
            new InviteVerifyRequest(TokenOf(created), "new@example.com", code == "000000" ? "111111" : "000000"));

        Assert.Equal("wrong", await CodeOf(wrong));
        Assert.Equal("Active", Assert.Single(await InvitesAsync()).Status);
    }

    [Fact]
    public async Task InviteMode_PlainLogin_UnknownEmail_DoesNotRegister()
    {
        var client = Anonymous();
        (await client.PostAsJsonAsync("/api/auth/code", new RequestCodeRequest("nobody@example.com"))).EnsureSuccessStatusCode();
        Assert.Empty(Services.GetRequiredService<DevMailbox>().Latest(5, "nobody@example.com"));
        Assert.Single(await GetAsync<List<UserAdminDto>>("/api/admin/users"));
    }

    [Fact]
    public async Task Disable_RevokesSessions_BlocksLogin_SaysDisabled_EnableRestores()
    {
        var user = await Factory.CreateUserAsync("b@test.local");
        var b = await Factory.CreateSignedInClientAsync("b@test.local");

        var disabled = await PutAsync<UserAdminDto>($"/api/admin/users/{user.Id}/disabled", new SetUserDisabledRequest(true));
        Assert.True(disabled.IsDisabled);

        var rejected = await b.GetAsync("/api/board");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        // Сессии отозваны — 401 без объяснения; объяснение «отключён» — у живой сессии отключённого (проверяем новой сессией ниже)
        var mailbox = Services.GetRequiredService<DevMailbox>();
        var lettersBefore = mailbox.Latest(100, "b@test.local").Count;
        ShiftClock(TimeSpan.FromSeconds(61));
        (await Anonymous().PostAsJsonAsync("/api/auth/code", new RequestCodeRequest("b@test.local"))).EnsureSuccessStatusCode();
        Assert.Equal(lettersBefore, mailbox.Latest(100, "b@test.local").Count); // кода нет

        await PutAsync<UserAdminDto>($"/api/admin/users/{user.Id}/disabled", new SetUserDisabledRequest(false));
        ShiftClock(TimeSpan.FromSeconds(122));
        var again = await Factory.CreateSignedInClientAsync("b@test.local");
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/board")).StatusCode);
    }

    [Fact]
    public async Task Disable_WhileSignedIn_Answers401Disabled()
    {
        var user = await Factory.CreateUserAsync("c@test.local");
        var c = await Factory.CreateSignedInClientAsync("c@test.local");

        // Отключение напрямую, без отзыва сессий (как если бы сессия была создана в момент отключения)
        await using (var scope = Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<Planner.Server.Data.Accounts.AccountsDbContext>();
            accounts.Users.Single(u => u.Id == user.Id).SetDisabled(true);
            await accounts.SaveChangesAsync();
        }

        var response = await c.GetAsync("/api/board");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("disabled", await CodeOf(response));
    }

    [Fact]
    public async Task Admin_CannotBeDisabled_NonAdmin_NoAdminScreen()
    {
        var users = await GetAsync<List<UserAdminDto>>("/api/admin/users");
        var admin = Assert.Single(users);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PutAsJsonAsync($"/api/admin/users/{admin.Id}/disabled", new SetUserDisabledRequest(true))).StatusCode);

        await Factory.CreateUserAsync("d@test.local");
        var d = await Factory.CreateSignedInClientAsync("d@test.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await d.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await d.PostAsJsonAsync("/api/admin/invites", new CreateInviteRequest(null))).StatusCode);
    }

    [Fact]
    public async Task InvalidDays_Is400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/admin/invites", new CreateInviteRequest(null, 5))).StatusCode);
    }
}

/// <summary>Открытая регистрация (I6): любой адрес получает код, после кода — аккаунт со стартовым набором.</summary>
public sealed class OpenRegistrationTests : IAsyncLifetime
{
    private readonly PlannerApiFactory _factory = new(settings: new Dictionary<string, string?> { [LoginService.RegistrationKey] = "Open" });

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task UnknownEmail_GetsCode_AndAccount()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/code", new RequestCodeRequest("free@example.com"))).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeRequest("free@example.com", _factory.LastCode("free@example.com")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var board = await client.GetFromJsonAsync<BoardDto>("/api/board");
        Assert.Contains(board!.Roles, r => r.Name == "Я");
        Assert.Equal(2, board.Categories.Count);
    }
}
