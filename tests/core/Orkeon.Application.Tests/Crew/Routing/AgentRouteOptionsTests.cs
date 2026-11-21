using Orkeon.Application.Crew.Routing;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Crew.Routing;

/// <summary>The ladder of <c>Orkeon:Decision:Route</c> (#434), judged at the host's start.</summary>
public sealed class AgentRouteOptionsTests
{
    private static string[] Rungs(string ladder) => ladder.Length == 0 ? [] : ladder.Split(',');

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData("")]
    [InlineData("small,large")]
    [InlineData("small,default,large")]
    public void ShouldFindNoProblem_WhenTheLadderIsAbsentOrHoldsDistinctProfiles(string ladder)
    {
        // Arrange
        var options = new AgentRouteOptions { Ladder = [.. Rungs(ladder)] };

        // Act
        var problems = AgentRouteOptions.Problems(options);

        // Assert
        Assert.Empty(problems);
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData("small", "two or more")]
    [InlineData("small, ", ":1 is blank")]
    [InlineData("small,Auto", ":1 is 'auto'")]
    [InlineData("small,large,SMALL", "more than once")]
    public void ShouldNameTheKey_WhenTheLadderIsNoLadder(string ladder, string expected)
    {
        // Arrange
        var options = new AgentRouteOptions { Ladder = [.. Rungs(ladder)] };

        // Act
        var problem = Assert.Single(AgentRouteOptions.Problems(options));

        // Assert
        Assert.Contains(LlmProfiles.RouteLadderKey, problem, StringComparison.Ordinal);
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }
}
