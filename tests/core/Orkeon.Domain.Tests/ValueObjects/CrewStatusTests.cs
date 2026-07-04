using Orkeon.Domain.Crew.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class CrewStatusTests
{
    [Fact]
    public void ShouldHaveExpectedCount_WhenGettingAll()
    {
        Assert.Equal(9, CrewStatus.All.Count);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Idle")]
    [InlineData("Initializing")]
    [InlineData("Executing")]
    [InlineData("Paused")]
    [InlineData(Completed)]
    [InlineData(Failed)]
    [InlineData("Cancelled")]
    [InlineData("Error")]
    public void ShouldParse_WhenUsingFromWithValidString(string value)
    {
        var status = CrewStatus.From(value);
        Assert.Equal(value, status.Value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidString()
    {
        Assert.Throws<ArgumentException>(() => CrewStatus.From("InvalidStatus"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingTryFromWithValidString()
    {
        var result = CrewStatus.TryFrom("Executing", out var status);
        Assert.True(result);
        Assert.Equal(CrewStatus.Executing, status);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingTryFromWithInvalidString()
    {
        var result = CrewStatus.TryFrom("InvalidStatus", out var status);
        Assert.False(result);
        Assert.Null(status);
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingSameInstances()
    {
#pragma warning disable CS1718 // Testing reflexive equality
        Assert.True(CrewStatus.Executing == CrewStatus.Executing);
#pragma warning restore CS1718
    }

    [Fact]
    public void ShouldConvertToString_WhenUsingImplicitConversion()
    {
        string value = CrewStatus.Executing;
        Assert.Equal("Executing", value);
    }
}
