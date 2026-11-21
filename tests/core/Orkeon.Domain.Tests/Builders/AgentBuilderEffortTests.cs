using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.Builders;

/// <summary>An agent built in C# that leaves its thinking level to System One (#439).</summary>
public sealed class AgentBuilderEffortTests
{
    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldLeaveTheLevelToSystemOneAndKeepTheSwitch_WhenTheAgentAsksForTheEffortAuto()
    {
        // Arrange
        var builder = new AgentBuilder().Role("Coder").Goal("Change code").Thinking(enabled: true, effort: "high");

        // Act
        var agent = builder.ThinkingEffortAuto().Build();

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true, AutoEffort = LlmAutoEffort.Any }, agent.LlmConfig!.Thinking);
    }
}
