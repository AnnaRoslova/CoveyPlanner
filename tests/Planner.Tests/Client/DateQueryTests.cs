using Planner.Client.Services;

namespace Planner.Tests.Client;

/// <summary>Переход к дате: что понимает поле календаря и быстрый поиск (batch-3.md, J4).</summary>
public class DateQueryTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Theory]
    [InlineData("12.03", "2026-03-12")]       // без года — текущий
    [InlineData("12.03.2027", "2027-03-12")]
    [InlineData("1.3.27", "2027-03-01")]
    [InlineData("12/03/2027", "2027-03-12")]
    [InlineData("2027-03-12", "2027-03-12")]
    [InlineData("12 марта", "2026-03-12")]
    [InlineData("12 МАРТА 2027", "2027-03-12")]
    [InlineData("12 мар", "2026-03-12")]
    [InlineData("12 окт", "2026-10-12")]
    [InlineData("  12 окт.  ", "2026-10-12")]
    [InlineData("3 сент 2027 г.", "2027-09-03")]
    [InlineData("1 мая", "2026-05-01")]
    [InlineData("12марта", "2026-03-12")]
    public void Day(string text, string expected)
    {
        var result = DateQuery.Parse(text, Today);

        Assert.Equal(new DateQueryResult(DateOnly.Parse(expected), IsMonth: false), result);
    }

    [Theory]
    [InlineData("март 2027", "2027-03-01")]
    [InlineData("март", "2026-03-01")]
    [InlineData("май", "2026-05-01")]
    [InlineData("Ноябрь 2026", "2026-11-01")]
    public void Month(string text, string expected) =>
        Assert.Equal(new DateQueryResult(DateOnly.Parse(expected), IsMonth: true), DateQuery.Parse(text, Today));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("31.02")]          // нет такого дня
    [InlineData("12.13")]
    [InlineData("12 мартабря")]
    [InlineData("ма")]             // слишком коротко для месяца
    [InlineData("12 ок")]
    [InlineData("купить хлеб")]
    [InlineData("12")]
    [InlineData("12.03.1850")]
    [InlineData("ремонт 2027")]
    public void NotUnderstood_Null(string? text) => Assert.Null(DateQuery.Parse(text, Today));
}
