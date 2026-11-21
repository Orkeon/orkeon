using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// <c>thinking: { enabled: auto }</c> and the raise after a failed attempt (#439): the answers offered, the thinking each
/// settles on, and the step above the current one.
/// </summary>
public sealed class LlmThinkingConfigEnabledAutoTests
{
    private static readonly string[] HostLevels = ["low", "medium", "high"];

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldOfferOffAndTheWrittenLevel_WhenOnlyTheSwitchIsLeftToSystemOne()
    {
        // Arrange
        var thinking = new LlmThinkingConfig { AutoEnabled = true, Effort = "high" };

        // Act
        var options = thinking.AutoOptions(HostLevels);

        // Assert
        Assert.Equal([LlmThinkingConfig.Off, "high"], options);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldOfferOffAndOn_WhenTheSwitchIsLeftToSystemOneAndNoLevelIsWritten()
    {
        // Arrange
        var thinking = new LlmThinkingConfig { AutoEnabled = true };

        // Act
        var options = thinking.AutoOptions(HostLevels);

        // Assert
        Assert.Equal([LlmThinkingConfig.Off, LlmThinkingConfig.On], options);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldOfferOffAndEveryAllowedLevel_WhenTheSwitchAndTheLevelAreBothLeftToSystemOne()
    {
        // Arrange
        var thinking = new LlmThinkingConfig { AutoEnabled = true, AutoEffort = LlmAutoEffort.Bounded(null, "medium") };

        // Act
        var options = thinking.AutoOptions(HostLevels);

        // Assert
        Assert.Equal([LlmThinkingConfig.Off, "low", "medium"], options);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldSwitchThinkingOff_WhenSystemOneAnswersOff()
    {
        // Arrange
        var thinking = new LlmThinkingConfig { AutoEnabled = true, Effort = "high" };

        // Act
        var settled = thinking.Settle(LlmThinkingConfig.Off, null);

        // Assert
        Assert.False(settled!.Enabled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldSwitchThinkingOnAtTheWrittenLevel_WhenSystemOneAnswersOn()
    {
        // Arrange
        var thinking = new LlmThinkingConfig { AutoEnabled = true, Effort = "high" };

        // Act
        var settled = thinking.Settle(LlmThinkingConfig.On, null);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true, Effort = "high" }, settled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldKeepTheStandingSwitch_WhenSystemOneAnswersNothingOnEnabledAuto()
    {
        // Arrange
        var thinking = new LlmThinkingConfig { AutoEnabled = true, Effort = "high" };

        // Act
        var settled = thinking.Settle(null, new LlmThinkingConfig { Enabled = false });

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = false, Effort = "high" }, settled);
    }

    public static TheoryData<string?, string?> Steps => new()
    {
        { LlmThinkingConfig.Off, "low" },
        { "low", "medium" },
        { "high", null },
        { null, null },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    [Trait("BR", "CH-439")]
    public void ShouldGiveTheNextAnswerUp_WhenAnAttemptFailedAtTheCurrentOne(string? current, string? expected)
    {
        // Arrange
        string[] options = [LlmThinkingConfig.Off, "low", "medium", "high"];

        // Act
        var above = LlmThinkingConfig.Above(current, options);

        // Assert
        Assert.Equal(expected, above);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldGiveTheFirstOfferedLevelAboveIt_WhenTheCurrentLevelIsNotOffered()
    {
        // Arrange
        string[] options = ["medium", "high"];

        // Act
        var above = LlmThinkingConfig.Above("low", options);

        // Assert
        Assert.Equal("medium", above);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldAnswerOff_WhenTheSettledThinkingIsSwitchedOff()
    {
        // Arrange
        var settled = new LlmThinkingConfig { Enabled = false, Effort = "high" };

        // Act
        var answer = LlmThinkingConfig.AnswerOf(settled, [LlmThinkingConfig.Off, "high"]);

        // Assert
        Assert.Equal(LlmThinkingConfig.Off, answer);
    }
}
