using Planner.Core.Time;

namespace Planner.Tests.Core;

public class WeekTests
{
    [Theory]
    [InlineData("2026-09-28", "2026-09-28")] // понедельник
    [InlineData("2026-10-01", "2026-09-28")] // четверг
    [InlineData("2026-10-04", "2026-09-28")] // воскресенье
    [InlineData("2027-01-01", "2026-12-28")] // через Новый год
    public void StartOf_ReturnsMonday(string date, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), Week.StartOf(DateOnly.Parse(date)));

    [Fact]
    public void Days_ReturnsSevenDaysFromMonday()
    {
        var days = Week.Days(new DateOnly(2026, 9, 28)).ToList();

        Assert.Equal(7, days.Count);
        Assert.Equal(DayOfWeek.Monday, days[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, days[6].DayOfWeek);
    }
}
