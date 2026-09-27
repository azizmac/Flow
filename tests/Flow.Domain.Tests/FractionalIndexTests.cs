using Flow.Domain.Ranking;
using Xunit;

namespace Flow.Domain.Tests;

public class FractionalIndexTests
{
    private static void AssertBetween(string? a, string key, string? b)
    {
        FractionalIndex.Validate(key);
        if (a is not null) Assert.True(string.CompareOrdinal(a, key) < 0, $"{a} < {key}");
        if (b is not null) Assert.True(string.CompareOrdinal(key, b) < 0, $"{key} < {b}");
    }

    [Fact]
    public void Empty_List_Starts_With_First()
    {
        Assert.Equal(FractionalIndex.First, FractionalIndex.Between(null, null));
    }

    [Theory]
    [InlineData(null, "a0", "Zz")]
    [InlineData("a0", null, "a1")]
    [InlineData("az", null, "b00")]
    [InlineData("a0", "a1", "a0V")]
    [InlineData("a0", "a0V", "a0G")]
    [InlineData("a1", "a2", "a1V")]
    [InlineData("Zz", "a0", "ZzV")]
    public void Between_Matches_Reference_Implementation(string? a, string? b, string expected)
    {
        Assert.Equal(expected, FractionalIndex.Between(a, b));
    }

    [Fact]
    public void Appending_Grows_Logarithmically()
    {
        var keys = FractionalIndex.Sequence(null, 10_000);

        for (var i = 1; i < keys.Count; i++)
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0);
        Assert.True(keys[^1].Length <= 4, keys[^1]);
    }

    [Fact]
    public void Prepending_Keeps_Order()
    {
        string? first = null;
        for (var i = 0; i < 500; i++)
        {
            var key = FractionalIndex.Between(null, first);
            AssertBetween(null, key, first);
            first = key;
        }
    }

    [Fact]
    public void Repeated_Insert_Into_One_Gap_Stays_Ordered_And_Grows_Slowly()
    {
        var a = "a0";
        var b = "a1";
        for (var i = 0; i < 200; i++)
        {
            var key = FractionalIndex.Between(a, b);
            AssertBetween(a, key, b);
            if (i % 2 == 0) b = key; else a = key;
        }

        Assert.True(a.Length < FractionalIndex.RebalanceLength, a);
    }

    [Theory]
    [InlineData("a1", "a0")]
    [InlineData("a0", "a0")]
    public void Wrong_Order_Is_Rejected(string a, string b)
    {
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between(a, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a00")]
    [InlineData("a0-")]
    [InlineData("!0")]
    public void Invalid_Keys_Are_Rejected(string key)
    {
        Assert.False(FractionalIndex.IsValid(key));
    }
}
