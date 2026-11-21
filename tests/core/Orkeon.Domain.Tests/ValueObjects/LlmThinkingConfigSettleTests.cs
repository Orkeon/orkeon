using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>The thinking a config on <c>effort: auto</c> runs on once System One has picked, or not (#439).</summary>
public sealed class LlmThinkingConfigSettleTests
{
    private static readonly LlmThinkingConfig Auto = new() { Enabled = true, AutoEffort = LlmAutoEffort.Any };

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRunWithThePickedLevel_WhenSystemOnePickedOne()
    {
        // Arrange
        var standing = new LlmThinkingConfig { Effort = "low" };

        // Act
        var settled = Auto.Settle("high", standing);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true, Effort = "high" }, settled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRunWithTheStandingLevelAndItsOwnSwitch_WhenSystemOnePickedNone()
    {
        // Arrange
        var standing = new LlmThinkingConfig { Enabled = false, Effort = "medium", BudgetTokens = 2048 };

        // Act
        var settled = Auto.Settle(null, standing);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true, Effort = "medium", BudgetTokens = 2048 }, settled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldLeaveTheProviderItsOwn_WhenNothingStandsAndNothingWasPicked()
    {
        // Arrange
        var auto = new LlmThinkingConfig { AutoEffort = LlmAutoEffort.Any };

        // Act
        var settled = auto.Settle(null, null);

        // Assert
        Assert.Null(settled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldKeepAWrittenLevel_WhenTheConfigLeavesNothingToSystemOne()
    {
        // Arrange
        var written = new LlmThinkingConfig { Effort = "max" };

        // Act
        var settled = written.Settle("low", null);

        // Assert
        Assert.Same(written, settled);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData(" AUTO ")]
    [Trait("BR", "CH-439")]
    public void ShouldReadTheMarker_WhenTheEffortIsAutoInAnyCase(string effort)
    {
        // Act
        var isAuto = LlmAutoEffort.IsAuto(effort);

        // Assert
        Assert.True(isAuto);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldKeepTheStandingBudget_WhenSystemOnePickedALevel()
    {
        // Arrange
        var standing = new LlmThinkingConfig { Effort = "low", BudgetTokens = 4000 };

        // Act
        var settled = Auto.Settle("high", standing);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true, Effort = "high", BudgetTokens = 4000 }, settled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldKeepThinkingOffAndDropTheMarker_WhenTheConfigWritesThinkingOffUnderEffortAuto()
    {
        // Arrange
        var off = new LlmThinkingConfig { Enabled = false, AutoEffort = LlmAutoEffort.Any };

        // Act
        var settled = off.Settle("high", new LlmThinkingConfig { Effort = "low" });

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = false }, settled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldKeepThinkingOffAndIgnoreThePick_WhenTheStandingThinkingIsWrittenOff()
    {
        // Arrange
        var auto = new LlmThinkingConfig { AutoEffort = LlmAutoEffort.Any };
        var standing = new LlmThinkingConfig { Enabled = false };

        // Act
        var settled = auto.Settle("high", standing);

        // Assert
        Assert.True(auto.IsHeldOffBy(standing));
        Assert.Equal(new LlmThinkingConfig { Enabled = false }, settled);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldLetThePickSwitchThinkingOn_WhenTheConfigWritesEnabledAutoOverAThinkingWrittenOff()
    {
        // Arrange
        var auto = new LlmThinkingConfig { AutoEnabled = true, AutoEffort = LlmAutoEffort.Any };
        var standing = new LlmThinkingConfig { Enabled = false };

        // Act
        var settled = auto.Settle("high", standing);

        // Assert
        Assert.False(auto.IsHeldOffBy(standing));
        Assert.Equal(new LlmThinkingConfig { Enabled = true, Effort = "high" }, settled);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("low", "medium")]
    [InlineData("max", "high")]
    [InlineData("medium", "medium")]
    public void ShouldClampTheStandingLevelIntoTheBounds_WhenSystemOnePickedNone(string standingLevel, string expected)
    {
        // Arrange
        var auto = new LlmThinkingConfig { AutoEffort = LlmAutoEffort.Bounded("medium", "high") };

        // Act
        var settled = auto.Settle(null, new LlmThinkingConfig { Effort = standingLevel });

        // Assert
        Assert.Equal(expected, settled?.Effort);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldLeaveTheProviderItsOwn_WhenNothingStandsUnderABoundedPick()
    {
        // Arrange
        var auto = new LlmThinkingConfig { AutoEffort = LlmAutoEffort.Bounded("medium", null) };

        // Act
        var settled = auto.Settle(null, null);

        // Assert: the provider's own level is unknown, and no bound is forced on it.
        Assert.Null(settled);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("High", "high")]
    [InlineData(" XHIGH ", "xhigh")]
    [InlineData("auto", null)]
    [InlineData("huge", null)]
    [InlineData(null, null)]
    public void ShouldReadALevelInAnyCase_WhenACrewWritesIt(string? written, string? expected)
    {
        // Act
        var level = LlmThinkingConfig.ParseEffort(written);

        // Assert
        Assert.Equal(expected, level);
    }
}
