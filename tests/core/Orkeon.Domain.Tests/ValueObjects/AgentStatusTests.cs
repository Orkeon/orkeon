using Orkeon.Domain.Agent.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class AgentStatusTests
{
    [Fact]
    public void ShouldHaveExpectedCount_WhenGettingAll()
    {
        Assert.Equal(6, AgentStatus.All.Count);
    }

    [Fact]
    public void ShouldReturnCorrectValue_WhenUsingToString()
    {
        Assert.Equal("Created", AgentStatus.Created.ToString());
        Assert.Equal("Idle", AgentStatus.Idle.ToString());
        Assert.Equal("Busy", AgentStatus.Busy.ToString());
        Assert.Equal("Unavailable", AgentStatus.Unavailable.ToString());
        Assert.Equal("Deactivated", AgentStatus.Deactivated.ToString());
        Assert.Equal("Error", AgentStatus.Error.ToString());
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Idle")]
    [InlineData("Busy")]
    [InlineData("Unavailable")]
    [InlineData("Deactivated")]
    [InlineData("Error")]
    public void ShouldParse_WhenUsingFromWithValidString(string value)
    {
        var status = AgentStatus.From(value);
        Assert.Equal(value, status.Value);
    }

    [Theory]
    [InlineData("created")]
    [InlineData("IDLE")]
    [InlineData("BuSy")]
    public void ShouldParseCaseInsensitive_WhenUsingFrom(string value)
    {
        var status = AgentStatus.From(value);
        Assert.NotNull(status);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidString()
    {
        Assert.Throws<ArgumentException>(() => AgentStatus.From("InvalidStatus"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingTryFromWithValidString()
    {
        var result = AgentStatus.TryFrom("Idle", out var status);
        Assert.True(result);
        Assert.Equal(AgentStatus.Idle, status);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingTryFromWithInvalidString()
    {
        var result = AgentStatus.TryFrom("InvalidStatus", out var status);
        Assert.False(result);
        Assert.Null(status);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingTryFromWithNull()
    {
        var result = AgentStatus.TryFrom(null, out var status);
        Assert.False(result);
        Assert.Null(status);
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingSameInstances()
    {
        var status1 = AgentStatus.Busy;
        var status2 = AgentStatus.Busy;
        Assert.True(status1 == status2);
        Assert.True(status1.Equals(status2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingDifferentInstances()
    {
        Assert.True(AgentStatus.Idle != AgentStatus.Busy);
        Assert.False(AgentStatus.Idle.Equals(AgentStatus.Busy));
    }

    [Fact]
    public void ShouldConvertToString_WhenUsingImplicitConversion()
    {
        string value = AgentStatus.Idle;
        Assert.Equal("Idle", value);
    }

    [Fact]
    public void ShouldContainAllValues_WhenUsingAllCollection()
    {
        var all = AgentStatus.All;
        Assert.Contains(AgentStatus.Created, all);
        Assert.Contains(AgentStatus.Idle, all);
        Assert.Contains(AgentStatus.Busy, all);
        Assert.Contains(AgentStatus.Unavailable, all);
        Assert.Contains(AgentStatus.Deactivated, all);
        Assert.Contains(AgentStatus.Error, all);
    }

    [Fact]
    public void ShouldHaveConsistentHashCodes_ForSameValues()
    {
        var hash1 = AgentStatus.Idle.GetHashCode();
        var hash2 = AgentStatus.Idle.GetHashCode();
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldHandleAllCases_WhenUsingSwitchExpression()
    {
        foreach (var status in AgentStatus.All)
        {
            var result = status switch
            {
                { Value: "Created" } => "Just created",
                { Value: "Idle" } => "Ready to work",
                { Value: "Busy" } => "Working hard",
                { Value: "Unavailable" } => "Not available",
                { Value: "Deactivated" } => "Turned off",
                { Value: "Error" } => "Something went wrong",
                _ => "Unknown"
            };
            Assert.NotEqual("Unknown", result);
        }
    }
}
