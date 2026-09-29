using Planner.Client.Services;

namespace Planner.Tests.Client;

public class TextSearchTests
{
    [Theory]
    [InlineData("Толстой, «Детство»", "детство", true)]
    [InlineData("Перебрать зимние вещи", "ЗИМ", true)]
    [InlineData("Ёлочные игрушки", "елоч", true)]   // ё = е
    [InlineData("Елка на балконе", "ёлка", true)]
    [InlineData("Камчатка", "байкал", false)]
    [InlineData("Что угодно", "", true)]
    public void Contains(string text, string query, bool expected) =>
        Assert.Equal(expected, TextSearch.Contains(text, query));

    [Fact]
    public void Split_MarksAllMatches_KeepingOriginalCase()
    {
        var segments = TextSearch.Split("Хлеб и ещё хлеб", "ХЛЕБ").ToList();

        Assert.Equal(
            [new TextSegment("Хлеб", true), new TextSegment(" и ещё ", false), new TextSegment("хлеб", true)],
            segments);
    }

    [Fact]
    public void Split_NoQuery_WholeText()
    {
        var segment = Assert.Single(TextSearch.Split("Камчатка", null));

        Assert.Equal(new TextSegment("Камчатка", false), segment);
    }
}
