using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>The level a task of an agent keeps across its executions when its effort is left to System One (#439).</summary>
public sealed class KeptThinkingTests
{
    private static readonly LlmThinkingConfig Auto = new() { AutoEffort = LlmAutoEffort.Any };

    private static LlmThinkingConfig At(string level) => new() { Effort = level };

    private static KeptThinking Kept(string level) => new(Auto, null, At(level), ["low", "medium", "high"]);

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldStillRaise_WhenAConcurrentExecutionSucceededAfterAFailedOne()
    {
        // Arrange
        var kept = Kept("low");
        kept.NoteAttempt(true);

        // Act
        kept.NoteAttempt(false);
        var raise = kept.RaiseAfterFailure();

        // Assert
        Assert.Equal<(string?, string?)?>(("low", "medium"), raise);
        Assert.Equal(At("medium"), kept.Current);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRaiseOncePerFailedExecution_WhenTwoExecutionsFailed()
    {
        // Arrange
        var kept = Kept("low");
        kept.NoteAttempt(true);
        kept.NoteAttempt(true);

        // Act
        var first = kept.RaiseAfterFailure();
        var second = kept.RaiseAfterFailure();
        var third = kept.RaiseAfterFailure();

        // Assert
        Assert.Equal<(string?, string?)?>(("low", "medium"), first);
        Assert.Equal<(string?, string?)?>(("medium", "high"), second);
        Assert.Null(third);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRaiseTheExecutionsOwnLevelAndNeverLowerTheKeptOne_WhenARoundFailsBelowIt()
    {
        // Arrange: another execution has already brought the kept level to high.
        var kept = Kept("low");
        kept.NoteAttempt(true);
        kept.NoteAttempt(true);
        kept.RaiseAfterFailure();
        kept.RaiseAfterFailure();

        // Act: an execution that started at low fails a round.
        var raise = kept.RaiseFrom(At("low"));

        // Assert
        Assert.Equal<(LlmThinkingConfig?, string?, string?)?>((At("medium"), "low", "medium"), raise);
        Assert.Equal(At("high"), kept.Current);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldTellNoRaise_WhenTheThinkingIsHeldOffBelow()
    {
        // Arrange
        var standing = new LlmThinkingConfig { Enabled = false };
        var kept = new KeptThinking(Auto, standing, Auto.Settle(null, standing), ["low", "medium", "high"]);
        kept.NoteAttempt(true);

        // Act
        var raise = kept.RaiseAfterFailure();

        // Assert
        Assert.Null(raise);
        Assert.Equal(new LlmThinkingConfig { Enabled = false }, kept.Current);
    }
}
