using Planner.Client.Services;

namespace Planner.Tests.Client;

/// <summary>Возраст мысли (batch-ux.md, BS3, BS5): полные месяцы, метка, подсказка, старая ли.</summary>
public class ThoughtAgeTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    [Theory]
    [InlineData(2026, 8, 14, 2)] // 14 авг → 21 окт
    [InlineData(2026, 8, 22, 1)] // до полных двух не хватает дня
    [InlineData(2026, 10, 21, 0)]
    [InlineData(2025, 7, 21, 15)]
    public void Months_AreFull(int y, int m, int d, int expected) =>
        Assert.Equal(expected, ThoughtAge.Months(new DateOnly(y, m, d), Today));

    [Theory]
    [InlineData(2026, 10, 9, "12 дн")]
    [InlineData(2026, 2, 21, "8 мес")]
    [InlineData(2025, 10, 21, "1 г")]
    [InlineData(2025, 7, 1, "1 г 3 мес")]
    public void Label_DaysMonthsYears(int y, int m, int d, string expected) =>
        Assert.Equal(expected, ThoughtAge.Label(new DateOnly(y, m, d), Today));

    [Fact]
    public void Tooltip_AgeAndSince_YearOnlyIfDifferent()
    {
        Assert.Equal("лежит 2 мес · с 14 авг", ThoughtAge.Tooltip(new DateOnly(2026, 8, 14), Today));
        Assert.Equal("лежит 1 г 3 мес · с 1 июл 2025", ThoughtAge.Tooltip(new DateOnly(2025, 7, 1), Today));
        Assert.Equal("записано сегодня", ThoughtAge.Tooltip(Today, Today));
    }

    [Fact]
    public void IsStale_TermFromLatestOfUpdatedAndKept()
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddMonths(-4);

        Assert.True(ThoughtAge.IsStale(old, null, 3, now));
        Assert.False(ThoughtAge.IsStale(old, now.AddDays(-10), 3, now)); // «Оставить» 10 дней назад
        Assert.False(ThoughtAge.IsStale(old, null, 6, now));
        Assert.True(ThoughtAge.IsStale(now.AddMonths(-3), null, 3, now)); // ровно срок — уже старая
    }
}
