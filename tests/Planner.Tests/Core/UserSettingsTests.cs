using Planner.Core.Entities;
using Planner.Core.Errors;

namespace Planner.Tests.Core;

public class UserSettingsTests
{
    private readonly UserSettings _settings = new(); // 07:00–23:00, шаг 30

    [Theory]
    [InlineData("09:00", "09:00")]
    [InlineData("09:29", "09:00")]
    [InlineData("09:45", "09:30")]
    [InlineData("05:00", "07:00")] // раньше начала дня
    [InlineData("23:10", "22:30")] // после конца — последний слот
    public void SnapStart(string input, string expected) =>
        Assert.Equal(TimeOnly.Parse(expected), _settings.SnapStart(TimeOnly.Parse(input)));

    [Theory]
    [InlineData("09:00", 10, 30)]  // меньше шага — один шаг
    [InlineData("09:00", 44, 30)]
    [InlineData("09:00", 45, 60)]
    [InlineData("22:00", 120, 60)] // не вылезает за конец дня
    public void NormalizeDuration(string start, int input, int expected) =>
        Assert.Equal(expected, _settings.NormalizeDuration(TimeOnly.Parse(start), input));

    [Theory]
    [InlineData(10, 9, 30)]
    [InlineData(0, 25, 30)]
    [InlineData(7, 23, 20)]
    public void Update_Invalid_Throws(int start, int end, int slot) =>
        Assert.Throws<DomainException>(() => _settings.Update(start, end, slot));

    [Fact]
    public void Update_FullDay_Allowed()
    {
        _settings.Update(0, 24, 60);

        Assert.Equal(new TimeOnly(23, 0), _settings.SnapStart(new TimeOnly(23, 59)));
    }
}
