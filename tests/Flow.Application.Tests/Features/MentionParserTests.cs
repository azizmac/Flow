using Flow.Application.Features.Tasks.Mentions;
using Xunit;

namespace Flow.Application.Tests.Features;

public class MentionParserTests
{
    [Fact]
    public void Should_FindMentions_LowercaseAndDistinct()
    {
        var result = MentionParser.Parse("Привет @Ilya, посмотри вместе с @aziz.m и @ILYA!");

        Assert.Equal(new[] { "aziz.m", "ilya" }, result.Order());
    }

    [Theory]
    [InlineData("пиши на mail@example.com")]
    [InlineData("путь /users/@ilya")]
    [InlineData("одиночный @ символ")]
    [InlineData("слишком коротко @a")]
    public void Should_Ignore_NonMentions(string body)
    {
        Assert.Empty(MentionParser.Parse(body));
    }

    [Fact]
    public void Should_Ignore_Mentions_InsideCode()
    {
        var body = "в коде `@ilya` и\n```\n@aziz\n```\nа вот @real — упоминание";

        Assert.Equal(new[] { "real" }, MentionParser.Parse(body));
    }

    [Fact]
    public void Should_Stop_At_TrailingPunctuation()
    {
        Assert.Equal(new[] { "ilya" }, MentionParser.Parse("(@ilya)."));
        Assert.Equal(new[] { "ilya" }, MentionParser.Parse("@ilya: да"));
    }

    [Fact]
    public void Should_Return_Empty_For_Empty()
    {
        Assert.Empty(MentionParser.Parse(""));
    }
}
