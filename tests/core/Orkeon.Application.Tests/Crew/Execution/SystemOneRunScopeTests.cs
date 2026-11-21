using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Crew;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// #429, #434: a run holds its crew's decision mode and the warnings System One's consumers say once per run; a
/// nested run holds its own, and outside a run each consumer keeps its own warnings.
/// </summary>
public sealed class SystemOneRunScopeTests
{
    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldSayAWarningOnce_WhenTwoConsumersOfOneRunAskForIt()
    {
        // Arrange
        using var run = SystemOneRunScope.Begin(CrewDecisionMode.Unspecified);
        RunWarnings.Of(new RunWarnings()).First("skills.circuit-off");

        // Act
        var again = RunWarnings.Of(new RunWarnings()).First("skills.circuit-off");

        // Assert
        Assert.False(again);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldSayAWarningAgain_WhenTheNextRunOpensItsScope()
    {
        // Arrange
        var own = new RunWarnings();
        using (SystemOneRunScope.Begin(CrewDecisionMode.Unspecified))
            RunWarnings.Of(own).First("route.circuit-off");
        using var next = SystemOneRunScope.Begin(CrewDecisionMode.Unspecified);

        // Act
        var said = RunWarnings.Of(own).First("route.circuit-off");

        // Assert
        Assert.True(said);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldKeepTheConsumersOwnWarnings_WhenNoRunIsInProgress()
    {
        // Arrange
        var own = new RunWarnings();
        RunWarnings.Of(own).First("prune.circuit-off");

        // Act
        var again = RunWarnings.Of(own).First("prune.circuit-off");

        // Assert
        Assert.False(again);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRestoreTheEnclosingRunsMode_WhenANestedRunEnds()
    {
        // Arrange
        using var outer = SystemOneRunScope.Begin(CrewDecisionMode.None);
        var inner = SystemOneRunScope.Begin(CrewDecisionMode.Optional);

        // Act
        inner.Dispose();

        // Assert
        Assert.Equal(CrewDecisionMode.None, SystemOneRunScope.Current?.Mode);
    }
}
