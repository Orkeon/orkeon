using Orkeon.Application.Crew.Compaction;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>#432: the output the trigger keeps free - the call's cap, else the model's documented one, at most half the window.</summary>
public sealed class ContextCompactionReservedOutputTests
{
    [Theory]
    [Trait("BR", "CH-432")]
    [InlineData(4_000, "unknown-model", 200_000, 4_000)]   // the call's own cap
    [InlineData(null, "gpt-5.6-sol", 400_000, 128_000)]    // the model's documented cap
    [InlineData(64_000, "gpt-5.6-sol", 100_000, 50_000)]   // never more than half the window
    public void ShouldReserveTheCallsCapOrTheModelsAtMostHalfTheWindow_WhenTheTriggerIsComputed(
        int? maxTokens, string model, int window, int expected)
    {
        // Arrange
        var reservedFor = (maxTokens, model, window);

        // Act
        var reserved = ContextCompaction.ReservedOutput(reservedFor.maxTokens, reservedFor.model, reservedFor.window);

        // Assert
        Assert.Equal(expected, reserved);
    }
}
