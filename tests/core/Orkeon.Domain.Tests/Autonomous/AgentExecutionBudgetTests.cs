using Orkeon.Domain.Autonomous;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Domain.Tests.Autonomous;

/// <summary>
/// <see cref="AgentExecutionBudget.ChargedTo"/> (#189): a budget that also charges another one, the
/// tally a delegated script run shares with its crew's run.
/// </summary>
public class AgentExecutionBudgetTests
{
    [Fact]
    public void ShouldRecordOnBothBudgets_WhenChargedToAnother()
    {
        // Arrange
        var crew = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 1_000, MaxSpawnedAgents = 5 };
        var run = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 1_000, MaxSpawnedAgents = 5, ChargedTo = crew };

        // Act
        run.RecordToolCall();
        run.RecordTokens(40);
        run.RecordSpawn();
        run.RecordDelegation();

        // Assert: delegation depth is the run's own, not spent from the crew's
        Assert.Equal(1, crew.CurrentToolCalls);
        Assert.Equal(40, crew.CurrentTokensConsumed);
        Assert.Equal(1, crew.CurrentSpawnedAgents);
        Assert.Equal(0, crew.CurrentDelegationDepth);
        Assert.Equal(1, run.CurrentToolCalls);
    }

    [Fact]
    public void ShouldThrowTheChargedBudgetsError_WhenTheChargedOneIsSpentFirst()
    {
        // Arrange
        var crew = new AgentExecutionBudget { MaxTokensConsumed = 100 };
        var first = new AgentExecutionBudget { MaxTokensConsumed = 1_000, ChargedTo = crew };
        var second = new AgentExecutionBudget { MaxTokensConsumed = 1_000, ChargedTo = crew };
        first.RecordTokens(50);
        second.RecordTokens(50);

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(first.ThrowIfExhausted);

        // Assert
        Assert.Equal(BudgetDimension.Tokens, ex.Dimension);
        Assert.Contains("(100/100)", ex.Message, StringComparison.Ordinal);
        Assert.True(second.IsExhausted);
        Assert.False(new AgentExecutionBudget { MaxTokensConsumed = 1_000 }.IsExhausted);
    }

    [Fact]
    public void ShouldNeverLoseACharge_WhenRunsChargeOneBudgetAtOnce()
    {
        // Arrange
        var crew = new AgentExecutionBudget { MaxToolCalls = int.MaxValue, MaxTokensConsumed = int.MaxValue };
        var runs = Enumerable.Range(0, 8)
            .Select(_ => new AgentExecutionBudget { MaxToolCalls = int.MaxValue, MaxTokensConsumed = int.MaxValue, ChargedTo = crew })
            .ToArray();

        // Act
        Parallel.ForEach(runs, run =>
        {
            for (var i = 0; i < 1_000; i++)
            {
                run.RecordToolCall();
                run.RecordTokens(2);
            }
        });

        // Assert
        Assert.Equal(8_000, crew.CurrentToolCalls);
        Assert.Equal(16_000, crew.CurrentTokensConsumed);
    }

    [Fact]
    public void ShouldPauseTheChargedClock_WhenTheRunsClockIsPaused()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var crew = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMinutes(5), TimeProvider = clock };
        var run = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMinutes(5), TimeProvider = clock, ChargedTo = crew };

        // Act
        using (run.PauseWallClock())
            clock.Advance(TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(1));

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(1), crew.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(1), run.Elapsed);
    }

    [Fact]
    public void ShouldEndTheRunOnTheChargedWallTime_WhenTheCrewClockRunsOut()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var crew = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMinutes(1), TimeProvider = clock };
        var run = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMinutes(5), TimeProvider = clock, ChargedTo = crew };
        clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(run.RecordToolCall);

        // Assert
        Assert.Equal(BudgetDimension.WallTime, ex.Dimension);
    }

    [Fact]
    public void ShouldNotCarryTheCharge_WhenCreatingChildBudget()
    {
        // Arrange
        var crew = new AgentExecutionBudget();
        var run = new AgentExecutionBudget { MaxToolCalls = 4, ChargedTo = crew };
        run.RecordToolCall();

        // Act
        var child = run.CreateChildBudget();

        // Assert
        Assert.Null(child.ChargedTo);
        Assert.Equal(3, child.MaxToolCalls);
    }
}
