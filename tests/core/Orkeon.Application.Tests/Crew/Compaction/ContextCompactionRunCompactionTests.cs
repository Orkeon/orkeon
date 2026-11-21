using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>
/// #429: in a run of a crew with <c>decision.mode: none</c>, an agent that leaves <c>prune</c> unset is out of the
/// host's pruning; the agent itself is never changed, so another crew's run keeps the host's setting.
/// </summary>
public sealed class ContextCompactionRunCompactionTests
{
    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldTakeTheAgentOutOfThePruning_WhenTheRunsCrewAsksSystemOneNothing()
    {
        // Arrange
        using var run = SystemOneRunScope.Begin(CrewDecisionMode.None);

        // Act
        var compaction = ContextCompaction.RunCompaction(own: null);

        // Assert
        Assert.False(compaction?.Prune);
    }

    [Theory]
    [Trait("BR", "CH-429")]
    [InlineData(CrewDecisionMode.Unspecified)]
    [InlineData(CrewDecisionMode.Optional)]
    [InlineData(CrewDecisionMode.Required)]
    public void ShouldFollowTheHostsPruning_WhenTheRunsCrewMayAskSystemOne(CrewDecisionMode mode)
    {
        // Arrange
        using var run = SystemOneRunScope.Begin(mode);

        // Act
        var compaction = ContextCompaction.RunCompaction(own: null);

        // Assert
        Assert.Null(compaction?.Prune);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldKeepTheAgentsOtherSettings_WhenTheRunsCrewAsksSystemOneNothing()
    {
        // Arrange
        using var run = SystemOneRunScope.Begin(CrewDecisionMode.None);
        var own = new AgentCompaction { Enabled = true, TriggerShare = 0.6 };

        // Act
        var compaction = ContextCompaction.RunCompaction(own);

        // Assert
        Assert.Equal(own with { Prune = false }, compaction);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldFollowTheHostsPruning_WhenNoRunIsInProgress()
    {
        // Arrange
        var own = new AgentCompaction { TriggerShare = 0.6 };

        // Act
        var compaction = ContextCompaction.RunCompaction(own);

        // Assert
        Assert.Same(own, compaction);
    }
}
