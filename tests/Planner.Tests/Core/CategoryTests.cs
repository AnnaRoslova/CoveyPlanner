using Planner.Core.Entities;
using Planner.Core.Errors;

namespace Planner.Tests.Core;

public class CategoryTests
{
    [Fact]
    public void Create_WithEmoji()
    {
        var category = Category.Create(" Идеи ", "💡");

        Assert.Equal("Идеи", category.Name);
        Assert.Equal("💡", category.Emoji);
        Assert.True(category.IsExpanded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyEmoji_MeansNoIcon(string? emoji) =>
        Assert.Null(Category.Create("Идеи", emoji).Emoji);

    [Fact]
    public void CompositeEmoji_Fits()
    {
        // Семья из ZWJ-последовательности — 11 UTF-16 символов
        var category = Category.Create("Семья", "👨‍👩‍👧‍👦");

        Assert.Equal("👨‍👩‍👧‍👦", category.Emoji);
    }

    [Fact]
    public void TooLongEmoji_Throws() =>
        Assert.Throws<DomainException>(() => Category.Create("Идеи", new string('x', Category.EmojiMaxLength + 1)));
}
