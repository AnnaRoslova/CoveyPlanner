using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planner.Server.Accounts;
using Planner.Server.Data.Accounts;
using Planner.Shared;

namespace Planner.Tests.Api;

/// <summary>Вход кодом, частота, CSRF и сессии (docs/specs/accounts.md, L, S, C, T3).</summary>
public class AuthApiTests : ApiTestBase
{
    /// <summary>Тесты кода — на отдельном пользователе: администратор уже вошёл в начале теста (и его код только что запрашивали).</summary>
    private const string Admin = "user@test.local";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.CreateUserAsync(Admin);
    }

    private HttpClient NewClient() => Factory.CreateClient();

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private Task<HttpResponseMessage> RequestCode(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/code", new RequestCodeRequest(email));

    private Task<HttpResponseMessage> Verify(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeRequest(email, code));

    private int Letters(string email) => Services.GetRequiredService<DevMailbox>().Latest(100, email).Count;

    [Fact]
    public async Task WithoutLogin_DataIs401_AndMeIs401()
    {
        var client = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ping")).StatusCode);
    }

    [Fact]
    public async Task Login_ByCode_SetsSafeCookie_AndMe()
    {
        var client = NewClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/141.0 Safari/537.36 Edg/141.0");
        (await RequestCode(client, "  User@Test.Local ")).EnsureSuccessStatusCode();
        var response = await Verify(client, Admin, Factory.LastCode(Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("planner.session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(Admin, me!.Email);
        Assert.False(me.IsAdmin);

        await using var scope = Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        Assert.Contains(await accounts.Sessions.Select(s => s.Device).ToListAsync(), d => d == "Edge · Windows");
        Assert.NotNull((await accounts.Users.SingleAsync(u => u.Email == Admin)).LastLoginAt);
    }

    [Fact]
    public async Task UnknownEmail_SameAnswer_NoLetter_CodeNeverWorks()
    {
        var client = NewClient();
        var known = await (await RequestCode(client, Admin)).Content.ReadFromJsonAsync<RequestCodeResponse>();
        var unknown = await RequestCode(client, "stranger@example.com");

        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(known, await unknown.Content.ReadFromJsonAsync<RequestCodeResponse>());
        Assert.Equal(0, Letters("stranger@example.com"));

        var wrong = await Verify(client, "stranger@example.com", "123456");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("Неверный код, осталось попыток: 4", (await ProblemOf(wrong)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task TypoInEmail_Is400()
    {
        var response = await RequestCode(NewClient(), "anna@");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("опечатка", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Code_StoredOnlyAsHash()
    {
        await RequestCode(NewClient(), Admin);
        var code = Factory.LastCode(Admin);

        await using var scope = Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().LoginCodes
            .OrderByDescending(c => c.Id).FirstAsync();
        Assert.Equal(64, stored.CodeHash!.Length);
        Assert.DoesNotContain(code, stored.CodeHash);
        Assert.True(File.Exists(Factory.Paths.HmacKeyFile));
    }

    [Fact]
    public async Task Code_LivesTenMinutes()
    {
        var client = NewClient();
        await RequestCode(client, Admin);
        var code = Factory.LastCode(Admin);
        ShiftClock(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));

        var response = await Verify(client, Admin, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("expired", (await ProblemOf(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task FiveWrongAttempts_BurnCode()
    {
        var client = NewClient();
        await RequestCode(client, Admin);
        var code = Factory.LastCode(Admin);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var left = 4; left >= 1; left--)
        {
            var attempt = await Verify(client, Admin, wrong);
            Assert.Equal($"Неверный код, осталось попыток: {left}", (await ProblemOf(attempt)).GetProperty("title").GetString());
        }

        var fifth = await Verify(client, Admin, wrong);
        Assert.Equal("Код больше не действует — запросите новый", (await ProblemOf(fifth)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, Admin, code)).StatusCode); // и верный уже не подходит
    }

    [Fact]
    public async Task NewCode_ReplacesPrevious_SpacesInCodeAreFine()
    {
        var client = NewClient();
        await RequestCode(client, Admin);
        var first = Factory.LastCode(Admin);
        ShiftClock(TimeSpan.FromSeconds(61));
        await RequestCode(client, Admin);
        var second = Factory.LastCode(Admin);

        if (first != second)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, Admin, first)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await Verify(client, Admin, $"{second[..3]} {second[3..]}")).StatusCode);
    }

    [Fact]
    public async Task Frequency_ByEmail_OncePerMinute_FivePerHour()
    {
        var client = NewClient();
        (await RequestCode(client, Admin)).EnsureSuccessStatusCode();

        var tooSoon = await RequestCode(client, Admin);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        var seconds = (await ProblemOf(tooSoon)).GetProperty("retryAfterSeconds").GetInt32();
        Assert.InRange(seconds, 1, 60);
        Assert.StartsWith("Новый код можно запросить через", (await ProblemOf(tooSoon)).GetProperty("title").GetString());

        for (var i = 2; i <= 5; i++)
        {
            ShiftClock(TimeSpan.FromSeconds(61 * (i - 1)));
            (await RequestCode(client, Admin)).EnsureSuccessStatusCode();
        }

        ShiftClock(TimeSpan.FromSeconds(61 * 5));
        var sixth = await RequestCode(client, Admin);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.StartsWith("Слишком много попыток, попробуйте через", (await ProblemOf(sixth)).GetProperty("title").GetString());
        Assert.Equal(5, Letters(Admin));
    }

    [Fact]
    public async Task Frequency_ByAddress_TwentyCodeRequestsPerHour()
    {
        var client = NewClient();
        // Один запрос с этого адреса уже был — вход администратора в начале теста
        for (var i = 0; i < 19; i++)
        {
            (await RequestCode(client, $"user{i}@example.com")).EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestCode(client, "user20@example.com")).StatusCode);
    }

    [Fact]
    public async Task Csrf_ChangeWithoutHeaderOrFromForeignOrigin_Is400()
    {
        var bare = Factory.CreateClient();
        bare.DefaultRequestHeaders.Remove(AuthSetup.CsrfHeader);
        Assert.Equal(HttpStatusCode.BadRequest, (await bare.PostAsJsonAsync("/api/auth/code", new RequestCodeRequest(Admin))).StatusCode);

        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/categories")
        {
            Content = JsonContent.Create(new CreateCategoryRequest("Чужая", null)),
        };
        foreign.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.SendAsync(foreign)).StatusCode);

        // Чтение без заголовка — можно; своё изменение — можно
        Assert.Equal(HttpStatusCode.Unauthorized, (await bare.GetAsync("/api/board")).StatusCode);
        Assert.True((await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Своя", null))).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Logout_ThisDeviceOnly_LogoutAll_Everywhere()
    {
        // Ещё два устройства администратора (новый код — не раньше чем через минуту)
        ShiftClock(TimeSpan.FromSeconds(61));
        var second = await Factory.CreateSignedInClientAsync();
        ShiftClock(TimeSpan.FromSeconds(122));
        var third = await Factory.CreateSignedInClientAsync();

        (await second.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/board")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/board")).StatusCode);

        (await third.PostAsync("/api/auth/logout-all", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await third.GetAsync("/api/board")).StatusCode);
    }

    [Fact]
    public async Task Session_SlidesWithWork_ButNotLongerThanAYear()
    {
        // Каждые 80 дней работы сессия продлевается…
        for (var day = 80; day <= 320; day += 80)
        {
            ShiftClock(TimeSpan.FromDays(day));
            Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/board")).StatusCode);
        }

        // …но через год с входа — вход заново
        ShiftClock(TimeSpan.FromDays(366));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/board")).StatusCode);
    }

    [Fact]
    public async Task Session_UnusedFor90Days_Expires()
    {
        ShiftClock(TimeSpan.FromDays(91));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/board")).StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_OnEveryResponse()
    {
        var response = await NewClient().GetAsync("/api/ping");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task DevMailbox_NotAvailable_WithoutLoopbackConnection()
    {
        // У тестового сервера нет сетевого адреса клиента — как у запроса «не с этого компьютера»
        Assert.Equal(HttpStatusCode.NotFound, (await NewClient().GetAsync("/dev/mailbox")).StatusCode);
        Assert.False((await NewClient().GetFromJsonAsync<AuthInfoDto>("/api/auth/info"))!.DevMailbox);
    }
}
