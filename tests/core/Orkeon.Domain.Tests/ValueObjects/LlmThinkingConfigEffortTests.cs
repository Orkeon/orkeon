using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// The level a provider sends for the one asked, from the levels its field knows: the level itself
/// when known, else the highest known level below it — never a silent drop, never a failed request.
/// </summary>
public class LlmThinkingConfigEffortTests
{
    [Theory]
    [InlineData("high", "high")]
    [InlineData("xhigh", "high")]
    [InlineData("max", "high")]
    [InlineData("low", "low")]
    public void ShouldSendTheNearestKnownLevelBelow_WhenTheFieldKnowsThree(string asked, string sent)
    {
        Assert.Equal(sent, LlmThinkingConfig.EffortFor(asked, LlmThinkingConfig.ThreeLevels));
    }

    [Theory]
    [InlineData("xhigh")]
    [InlineData("max")]
    public void ShouldSendTheLevelAsked_WhenTheFieldKnowsIt(string asked)
    {
        Assert.Equal(asked, LlmThinkingConfig.EffortFor(asked, LlmThinkingConfig.Levels));
    }

    [Fact]
    public void ShouldSkipAnUnknownLevelInBetween_WhenTheFieldKnowsMaxButNotXhigh()
    {
        Assert.Equal("high", LlmThinkingConfig.EffortFor("xhigh", ["low", "medium", "high", "max"]));
    }

    [Fact]
    public void ShouldSendTheLowestKnownLevel_WhenNoneIsBelow()
    {
        Assert.Equal("medium", LlmThinkingConfig.EffortFor("low", ["medium", "high"]));
    }

    [Fact]
    public void ShouldDefaultTheDeclaredLevelsToThree()
    {
        Assert.Equal(LlmThinkingConfig.ThreeLevels, new LlmProviderCapabilities().EffortLevels);
    }
}
