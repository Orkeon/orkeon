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

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldBoundThePick_WhenTheAgentAsksForTheEffortAutoWithinLevels()
    {
        // Arrange
        var builder = new AgentBuilder().Role("Coder").Goal("Change code");

        // Act
        var agent = builder.ThinkingEffortAuto(min: "low", max: "high").Build();

        // Assert
        Assert.Equal(LlmAutoEffort.Bounded("low", "high"), agent.LlmConfig!.Thinking!.AutoEffort);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldLeaveTheSwitchToSystemOneAndKeepTheLevel_WhenTheAgentAsksForTheEnabledAuto()
    {
        // Arrange
        var builder = new AgentBuilder().Role("Coder").Goal("Change code").Thinking(enabled: true, effort: "high");

        // Act
        var agent = builder.ThinkingEnabledAuto().Build();

        // Assert
        Assert.Equal(new LlmThinkingConfig { AutoEnabled = true, Effort = "high" }, agent.LlmConfig!.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRefuseTheEffortAuto_WhenTheAgentsThinkingIsSwitchedOff()
    {
        // Arrange
        var builder = new AgentBuilder().Role("Coder").Goal("Change code").Thinking(false);

        // Act
        var error = Record.Exception(() => builder.ThinkingEffortAuto());

        // Assert
        var refused = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("ThinkingEnabledAuto()", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldLeaveTheEffortToSystemOne_WhenTheSwitchIsLeftToItFirst()
    {
        // Act
        var agent = new AgentBuilder().Role("Coder").Goal("Change code").Thinking(false).ThinkingEnabledAuto().ThinkingEffortAuto().Build();

        // Assert
        Assert.Equal(new LlmThinkingConfig { AutoEnabled = true, AutoEffort = LlmAutoEffort.Any }, agent.LlmConfig!.Thinking);
    }
}
