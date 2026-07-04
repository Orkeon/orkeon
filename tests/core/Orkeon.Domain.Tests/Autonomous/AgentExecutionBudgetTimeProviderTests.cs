using Orkeon.Domain.Autonomous;

namespace Orkeon.Domain.Tests.Autonomous;

/// <summary>
/// Proves that <see cref="AgentExecutionBudget"/>'s wall-time dimension is testable
/// through an injected <see cref="TimeProvider"/> — advancing virtual time instead of
/// sleeping for real. (R4.12 / ANT-015.)
/// </summary>
public class AgentExecutionBudgetTimeProviderTests
{
    [Fact]
    public void ShouldExhaustWallTime_WhenVirtualClockAdvancesPastMax_WithoutRealWaiting()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var budget = new AgentExecutionBudget
        {
            MaxWallTime = TimeSpan.FromMinutes(5),
            TimeProvider = clock
        };

        Assert.False(budget.IsExhausted);

        // Act — advance virtual time past the wall-time limit (no Thread.Sleep)
        clock.Advance(TimeSpan.FromMinutes(6));

        // Assert
        Assert.True(budget.IsExhausted);
        Assert.True(budget.Elapsed >= TimeSpan.FromMinutes(6));
        var ex = Assert.Throws<BudgetExhaustedException>(budget.AssertWallTime);
        Assert.Equal(BudgetDimension.WallTime, ex.Dimension);
    }

    [Fact]
    public void ShouldNotExhaustWallTime_BeforeVirtualClockReachesMax()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var budget = new AgentExecutionBudget
        {
            MaxWallTime = TimeSpan.FromMinutes(5),
            TimeProvider = clock
        };

        // Act — advance, but stay under the limit
        clock.Advance(TimeSpan.FromMinutes(4));

        // Assert — no exception, not exhausted
        var exception = Record.Exception(budget.AssertWallTime);
        Assert.Null(exception);
        Assert.False(budget.IsExhausted);
    }

    [Fact]
    public void ShouldInheritInjectedClock_WhenCreatingChildBudget()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var parent = new AgentExecutionBudget
        {
            MaxWallTime = TimeSpan.FromMinutes(10),
            TimeProvider = clock
        };
        clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        var child = parent.CreateChildBudget();
        clock.Advance(TimeSpan.FromMinutes(9)); // total elapsed: 11 min

        // Assert — the child uses the same injected clock and is now exhausted
        Assert.True(child.IsExhausted);
    }

    /// <summary>
    /// Minimal manual <see cref="TimeProvider"/> whose timestamp only advances when
    /// <see cref="Advance"/> is called, enabling deterministic wall-time tests.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan delta)
        {
            _timestamp += (long)(delta.TotalSeconds * TimestampFrequency);
        }
    }
}
