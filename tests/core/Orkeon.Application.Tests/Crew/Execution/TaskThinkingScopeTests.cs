using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>The thinking a task's calls run on when its config leaves the effort to System One (#439).</summary>
public sealed class TaskThinkingScopeTests
{
    private static LlmConfig AutoConfig() =>
        LlmConfig.OnProfile() with { Thinking = new LlmThinkingConfig { Enabled = true, AutoEffort = LlmAutoEffort.Any } };

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRunOnTheSettledThinking_WhenATaskScopeIsOpen()
    {
        // Arrange
        using var scope = TaskThinkingScope.Begin(new LlmThinkingConfig { Effort = "high" });

        // Act
        var resolved = TaskThinkingScope.Apply(AutoConfig());

        // Assert
        Assert.Equal(new LlmThinkingConfig { Effort = "high" }, resolved.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldDropTheMarkerAndKeepTheSwitch_WhenNoTaskScopeIsOpen()
    {
        // Arrange
        var config = AutoConfig();

        // Act
        var resolved = TaskThinkingScope.Apply(config);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true }, resolved.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldLeaveTheConfigAlone_WhenItsThinkingLeavesNothingToSystemOne()
    {
        // Arrange
        var config = LlmConfig.OnProfile() with { Thinking = new LlmThinkingConfig { Effort = "medium" } };
        using var scope = TaskThinkingScope.Begin(new LlmThinkingConfig { Effort = "high" });

        // Act
        var resolved = TaskThinkingScope.Apply(config);

        // Assert
        Assert.Same(config, resolved);
    }
}
