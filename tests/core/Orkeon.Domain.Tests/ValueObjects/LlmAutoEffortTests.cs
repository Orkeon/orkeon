using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>The bounds an agent or a task puts on System One's pick of a thinking level (#439).</summary>
public sealed class LlmAutoEffortTests
{
    private static readonly string[] HostLevels = ["low", "medium", "high"];

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldOfferTheHostLevelsInsideTheBounds_WhenTheMarkerIsBounded()
    {
        // Arrange
        var auto = LlmAutoEffort.Bounded("medium", "max");

        // Act
        var allowed = auto.Allowed(HostLevels);

        // Assert
        Assert.Equal(["medium", "high"], allowed);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldOfferNoLevel_WhenTheBoundsKeepNoneOfTheHostLevels()
    {
        // Arrange
        var auto = LlmAutoEffort.Bounded("xhigh", null);

        // Act
        var allowed = auto.Allowed(HostLevels);

        // Assert
        Assert.Empty(allowed);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldBeTheUnboundedMarker_WhenNoBoundIsGiven()
    {
        // Act
        var auto = LlmAutoEffort.Bounded(null, null);

        // Assert
        Assert.Same(LlmAutoEffort.Any, auto);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRefuseTheBounds_WhenTheLowestIsAboveTheHighest()
    {
        // Act
        var error = Assert.Throws<ArgumentException>(() => LlmAutoEffort.Bounded("high", "low"));

        // Assert
        Assert.Contains("min is at most max", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRefuseTheBound_WhenItIsNotALevel()
    {
        // Act
        var error = Record.Exception(() => LlmAutoEffort.Bounded("huge", null));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldDescribeTheBoundsAsACrewWritesThem_WhenBothAreSet()
    {
        // Arrange
        var auto = LlmAutoEffort.Bounded("low", "high");

        // Act
        var described = auto.DescribeBounds();

        // Assert
        Assert.Equal("min: low, max: high", described);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("low", "medium")]
    [InlineData("medium", "medium")]
    [InlineData("xhigh", "high")]
    [InlineData("none", "none")]
    [InlineData(null, null)]
    public void ShouldBringALevelInsideTheBounds_WhenItLiesOutside(string? level, string? expected)
    {
        // Act
        var clamped = LlmAutoEffort.Bounded("medium", "high").Clamp(level);

        // Assert
        Assert.Equal(expected, clamped);
    }
}
