using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class TaskCodeTests
{
    [Fact]
    public void Create_Should_FormatAsKeyDashNumber()
    {
        var code = TaskCode.Create("FLW", 42);

        Assert.Equal("FLW-42", code.Value);
        Assert.Equal("FLW-42", code.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Should_Throw_When_NumberIsNotPositive(int invalidNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskCode.Create("FLW", invalidNumber));
    }

    [Fact]
    public void FromValue_Should_WrapStoredString()
    {
        var code = TaskCode.FromValue("FLW-7");

        Assert.Equal("FLW-7", code.Value);
    }
}
