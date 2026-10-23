using Planner.Core.Accounts;
using Planner.Core.Errors;

namespace Planner.Tests.Core;

/// <summary>Почта пользователя (docs/specs/accounts.md, L6).</summary>
public class UserTests
{
    [Theory]
    [InlineData("anna@example.com", "anna@example.com")]
    [InlineData("  Anna@Example.COM ", "anna@example.com")]
    [InlineData("а.б+план@почта.рф", "а.б+план@почта.рф")]
    public void NormalizeEmail_TrimsAndLowercases(string email, string expected) =>
        Assert.Equal(expected, User.NormalizeEmail(email));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("anna")]
    [InlineData("@example.com")]
    [InlineData("anna@")]
    [InlineData("an na@example.com")]
    [InlineData("anna@@example.com")]
    [InlineData("a@b@example.com")]
    public void NormalizeEmail_RejectsTypos(string? email) =>
        Assert.Throws<DomainException>(() => User.NormalizeEmail(email));

    [Fact]
    public void NormalizeEmail_RejectsTooLong() =>
        Assert.Throws<DomainException>(() => User.NormalizeEmail(new string('a', 250) + "@x.ru"));

    [Fact]
    public void Create_GivesNewIdAndNormalizedEmail()
    {
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var first = User.Create("Anna@Example.com", isAdmin: true, now);
        var second = User.Create("oleg@example.com", isAdmin: false, now);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("anna@example.com", first.Email);
        Assert.True(first.IsAdmin);
        Assert.False(second.IsAdmin);
        Assert.Equal(now, first.CreatedAt);
        Assert.Null(first.LastLoginAt);
    }
}
