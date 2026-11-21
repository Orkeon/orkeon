using Orkeon.Application.Crew.Routing;

namespace Orkeon.Application.Tests.Crew.Routing;

/// <summary>The thinking levels a host allows System One to pick among (<c>Orkeon:Decision:Effort:Levels</c>, #439).</summary>
public sealed class AgentEffortOptionsTests
{
    public static TheoryData<string[], string> Refused => new()
    {
        { ["low", " "], "Orkeon:Decision:Effort:Levels:1 is blank" },
        { ["auto"], "the name of the pick itself" },
        { ["huge"], "'huge', which is not a thinking level" },
        { ["High"], "'High', which is not a thinking level" },
        { ["low", "low"], "names the level 'low' more than once" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    [Trait("BR", "CH-439")]
    public void ShouldRefuseTheLevels_WhenOneIsNotAKnownLevelOnce(string[] levels, string expected)
    {
        // Arrange
        var options = new AgentEffortOptions { Levels = levels };

        // Act
        var problems = AgentEffortOptions.Problems(options);

        // Assert
        Assert.Contains(expected, Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldAllowLowMediumAndHigh_WhenTheHostListsNoLevel()
    {
        // Arrange
        var options = new AgentEffortOptions();

        // Act
        var levels = AgentEffortOptions.AllowedLevels(options);

        // Assert
        Assert.Equal(["low", "medium", "high"], levels);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldAllowTheListedLevelsInTheirOrder_WhenTheHostListsSome()
    {
        // Arrange
        var options = new AgentEffortOptions { Levels = [" high", "max"] };

        // Act
        var levels = AgentEffortOptions.AllowedLevels(options);

        // Assert
        Assert.Equal(["high", "max"], levels);
    }
}
