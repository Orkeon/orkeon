using Orkeon.Domain.Agent;

namespace Orkeon.Domain.Tests.Builders;

/// <summary>#432: an agent's compaction override - carried as given, refused outside (0, 1].</summary>
public sealed class AgentBuilderCompactionTests
{
    private static AgentBuilder MinimalAgent() =>
        new AgentBuilder().Role("Reader").Goal("Read the pages");

    [Fact]
    [Trait("BR", "CH-432")]
    public void ShouldCarryTheOverride_WhenTheAgentSetsOne()
    {
        // Arrange
        var compaction = new AgentCompaction { Enabled = false, TriggerShare = 0.6 };

        // Act
        var agent = MinimalAgent().Compaction(compaction).Build();

        // Assert
        Assert.Equal(compaction, agent.Compaction);
    }

    [Theory]
    [Trait("BR", "CH-432")]
    [InlineData(0.0)]
    [InlineData(-0.2)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void ShouldRefuseTheOverride_WhenTheTriggerShareIsNotAShareOfTheWindow(double share)
    {
        // Arrange
        var builder = MinimalAgent();

        // Act
        var refusal = Record.Exception(() => builder.Compaction(new AgentCompaction { TriggerShare = share }));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }
}
