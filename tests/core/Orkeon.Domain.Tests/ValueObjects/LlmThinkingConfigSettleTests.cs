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
}
