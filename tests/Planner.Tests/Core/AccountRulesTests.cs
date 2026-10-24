using Planner.Core.Accounts;

namespace Planner.Tests.Core;

/// <summary>Правила кода входа, частоты, сессии и подписи устройства (docs/specs/accounts.md, L2–L5, S2, S5).</summary>
public class AccountRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Code_IsSixDigits()
    {
        for (var i = 0; i < 50; i++)
        {
            Assert.Matches("^[0-9]{6}$", LoginCode.Generate());
        }
    }

    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData(" 12-34-56 ", "123456")]
    [InlineData("Код входа: 123 456", "123456")]
    [InlineData(null, "")]
    public void Clean_KeepsDigits(string? input, string expected) => Assert.Equal(expected, LoginCode.Clean(input));

    [Fact]
    public void Check_Ok_ThenSpent()
    {
        var code = LoginCode.Create("a@b.ru", "hash", Now);
        Assert.Equal(LoginCheck.Ok, code.Check("hash", Now));
        Assert.Equal(LoginCheck.Expired, code.Check("hash", Now));
    }

    [Fact]
    public void Check_FiveWrong_Burns()
    {
        var code = LoginCode.Create("a@b.ru", "hash", Now);
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(LoginCheck.Wrong, code.Check("other", Now));
            Assert.Equal(5 - i, code.RemainingAttempts);
        }

        Assert.Equal(LoginCheck.Burned, code.Check("other", Now));
        Assert.Equal(LoginCheck.Expired, code.Check("hash", Now));
    }

    [Fact]
    public void Check_AfterTenMinutes_Expired()
    {
        var code = LoginCode.Create("a@b.ru", "hash", Now);
        Assert.Equal(LoginCheck.Ok, LoginCode.Create("a@b.ru", "hash", Now).Check("hash", Now.AddMinutes(10).AddSeconds(-1)));
        Assert.Equal(LoginCheck.Expired, code.Check("hash", Now.AddMinutes(10)));
    }

    [Fact]
    public void Check_UnregisteredEmail_NeverOk()
    {
        var code = LoginCode.Create("a@b.ru", null, Now);
        Assert.Equal(LoginCheck.Wrong, code.Check("anything", Now));
    }

    [Fact]
    public void Supersede_EndsPreviousCode()
    {
        var code = LoginCode.Create("a@b.ru", "hash", Now);
        code.Supersede(Now.AddMinutes(1));
        Assert.Equal(LoginCheck.Expired, code.Check("hash", Now.AddMinutes(1)));
    }

    [Fact]
    public void Throttle_OncePerMinute_FivePerHour()
    {
        Assert.Null(LoginThrottle.RetryAfter([], Now));
        Assert.Equal(TimeSpan.FromSeconds(45), LoginThrottle.RetryAfter([Now.AddSeconds(-15)], Now));
        Assert.Null(LoginThrottle.RetryAfter([Now.AddSeconds(-60)], Now));

        DateTime[] five = [.. Enumerable.Range(1, 5).Select(i => Now.AddMinutes(-10 * i))];
        Assert.Equal(TimeSpan.FromMinutes(10), LoginThrottle.RetryAfter(five, Now)); // самый старый из пяти — 50 мин назад
        Assert.Null(LoginThrottle.RetryAfter([.. five.Select(t => t.AddMinutes(-20))], Now)); // старше часа не считаются
    }

    [Fact]
    public void Session_SlidesButCapsAtAYear()
    {
        var session = Session.Create(Guid.NewGuid(), "Edge · Windows", Now);
        Assert.Equal(Now.AddDays(90), session.ExpiresAt);
        Assert.False(session.Touch(Now.AddMinutes(4)));
        Assert.True(session.Touch(Now.AddDays(80)));
        Assert.Equal(Now.AddDays(170), session.ExpiresAt);

        session.Touch(Now.AddDays(330));
        Assert.Equal(Now.AddDays(365), session.ExpiresAt);
        Assert.False(session.IsActive(Now.AddDays(365)));
    }

    [Fact]
    public void Session_Revoked_IsNotActive()
    {
        var session = Session.Create(Guid.NewGuid(), "x", Now);
        session.Revoke(Now);
        Assert.False(session.IsActive(Now));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36 Edg/141.0", "Edge · Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1", "Safari · iPhone")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/141.0 Mobile Safari/537.36", "Chrome · Android")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.0; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox · Mac")]
    [InlineData(null, "Браузер")]
    public void DeviceLabel_BrowserAndSystem(string? userAgent, string expected) =>
        Assert.Equal(expected, DeviceLabel.FromUserAgent(userAgent));
}
