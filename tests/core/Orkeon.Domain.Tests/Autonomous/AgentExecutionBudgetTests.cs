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
    public void ShouldCarryTheChargeAndCapByTheChargedRemainder_WhenCreatingChildBudget()
    {
        // Arrange: the crew has 2 tool calls left once the run spent one, the run 99; the crew 50 tokens and 1 spawn left
        var clock = new ManualTimeProvider();
        var crew = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 500, MaxSpawnedAgents = 2, MaxWallTime = TimeSpan.FromMinutes(2), TimeProvider = clock };
        crew.RecordToolCall();
        crew.RecordSpawn();
        crew.RecordTokens(450);
        for (var i = 0; i < 6; i++) crew.RecordToolCall();
        var run = new AgentExecutionBudget { MaxToolCalls = 100, MaxTokensConsumed = 10_000, MaxSpawnedAgents = 5, MaxWallTime = TimeSpan.FromMinutes(5), TimeProvider = clock, ChargedTo = crew };
        run.RecordToolCall();
        clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var child = run.CreateChildBudget();
        child.RecordToolCall();

        // Assert: what the child spends is charged to the crew, through the charge it carries
        Assert.Same(crew, child.ChargedTo);
        Assert.Equal(2, child.MaxToolCalls);
        Assert.Equal(100, child.MaxTokensConsumed);
        Assert.Equal(1, child.MaxSpawnedAgents);
        Assert.Equal(TimeSpan.FromMinutes(1), child.MaxWallTime);
        Assert.Equal(9, crew.CurrentToolCalls);
    }

    [Fact]
    public void ShouldKeepTheChildsOwnAllowances_WhenCreatingChildBudgetOfABudgetChargingNone()
    {
        // Arrange
        var run = new AgentExecutionBudget { MaxToolCalls = 4 };
        run.RecordToolCall();

        // Act
        var child = run.CreateChildBudget();

        // Assert
        Assert.Null(child.ChargedTo);
        Assert.Equal(3, child.MaxToolCalls);
    }

    [Fact]
    public void ShouldNotChargeACallTheRunRefuses_WhenTheRunsOwnLimitIsSpent()
    {
        // Arrange: crew 4 tool calls, run B 1, sibling C unbounded but charging the crew
        var crew = new AgentExecutionBudget { MaxToolCalls = 4 };
        var b = new AgentExecutionBudget { MaxToolCalls = 1, ChargedTo = crew };
        var c = new AgentExecutionBudget { MaxToolCalls = 100, ChargedTo = crew };
        b.RecordToolCall();

        // Act
        var refused = Assert.Throws<BudgetExhaustedException>(b.RecordToolCall);
        for (var i = 0; i < 3; i++) c.RecordToolCall();
        var crewRefused = Assert.Throws<BudgetExhaustedException>(c.RecordToolCall);

        // Assert: B's refused call never reached the crew, so C got the three left
        Assert.Equal(BudgetDimension.ToolCalls, refused.Dimension);
        Assert.Equal("Agent exceeded 4 tool calls.", crewRefused.Message);
        Assert.Equal(3, c.CurrentToolCalls);
    }

    [Fact]
    public void ShouldTakeTheRunsCountBack_WhenTheChargedBudgetRefusesASpawn()
    {
        // Arrange
        var crew = new AgentExecutionBudget { MaxSpawnedAgents = 1 };
        var run = new AgentExecutionBudget { MaxSpawnedAgents = 5, ChargedTo = crew };
        var sibling = new AgentExecutionBudget { MaxSpawnedAgents = 5, ChargedTo = crew };
        sibling.RecordSpawn();

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(run.RecordSpawn);

        // Assert
        Assert.Equal("Agent exceeded 1 spawned sub-agents.", ex.Message);
        Assert.Equal(0, run.CurrentSpawnedAgents);
    }

    [Fact]
    public void ShouldThrowOnTheChargedWallTime_WhenAssertWallTimeIsCalledOnTheRun()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var crew = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMinutes(1), TimeProvider = clock };
        var run = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMinutes(5), TimeProvider = clock, ChargedTo = crew };
        clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(run.AssertWallTime);

        // Assert
        Assert.Equal(BudgetDimension.WallTime, ex.Dimension);
    }

    [Fact]
    public void ShouldCarryTheChargedSnapshot_WhenTheChargedBudgetIsTheSpentOne()
    {
        // Arrange
        var crew = new AgentExecutionBudget { MaxToolCalls = 1 };
        var run = new AgentExecutionBudget { MaxToolCalls = 10, ChargedTo = crew };
        run.RecordToolCall();

        // Act
        var snapshot = run.ToSnapshot();

        // Assert: exhausted with its own count under its limit, and the charged snapshot tells why
        Assert.True(snapshot.IsExhausted);
        Assert.Equal(1, snapshot.ToolCalls);
        Assert.NotNull(snapshot.Charged);
        Assert.True(snapshot.Charged.IsExhausted);
        Assert.Equal(1, snapshot.Charged.MaxToolCalls);
        Assert.Null(crew.ToSnapshot().Charged);
    }
}
