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
        Assert.Equal("Token budget exhausted: 100 tokens sent and received over 2 calls (limit 100)", ex.Message);
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

    [Fact]
    public void ShouldNameTheRunningSumAndTheCalls_WhenTheTokenBudgetIsPassed()
    {
        // Arrange
        var run = new AgentExecutionBudget { MaxTokensConsumed = 400_000 };
        run.RecordTokens(200_000);

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(() => run.RecordTokens(225_303));

        // Assert: the tokens stay recorded, and the message says what it summed
        Assert.Equal(BudgetDimension.Tokens, ex.Dimension);
        Assert.Equal("Token budget exhausted: 425303 tokens sent and received over 2 calls (limit 400000)", ex.Message);
        Assert.Equal(425_303, run.CurrentTokensConsumed);
        Assert.Equal(2, run.CurrentTokenCalls);
    }

    [Fact]
    public void ShouldSayOneCall_WhenASingleCallSpendsTheTokenBudget()
    {
        // Arrange
        var run = new AgentExecutionBudget { MaxTokensConsumed = 100 };

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(() => run.RecordTokens(150));

        // Assert
        Assert.Equal("Token budget exhausted: 150 tokens sent and received over 1 call (limit 100)", ex.Message);
    }
    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldKeepTheTokens_WhenTheWallTimeHasPassed()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var budget = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMinutes(5), TimeProvider = clock };
        clock.Advance(TimeSpan.FromMinutes(6));

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(() => budget.RecordTokens(40));

        // Assert: the call happened and was paid, so its tokens count
        Assert.Equal(BudgetDimension.WallTime, ex.Dimension);
        Assert.Equal(40, budget.CurrentTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldCountAChildsTokensOncePerBudget_WhenRecordedOnTheChildAndItsParent()
    {
        // Arrange: the child carries the run's charge, the crew, as the run does
        var crew = new AgentExecutionBudget { MaxTokensConsumed = 10_000 };
        var run = new AgentExecutionBudget { MaxTokensConsumed = 10_000, ChargedTo = crew };
        var child = run.CreateChildBudget();

        // Act
        run.RecordChildTokens(child, 100);

        // Assert
        Assert.Equal(100, child.CurrentTokensConsumed);
        Assert.Equal(100, run.CurrentTokensConsumed);
        Assert.Equal(100, crew.CurrentTokensConsumed);
        Assert.Equal(1, crew.CurrentTokenCalls);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldThrowTheParentsRefusal_WhenAChildsTokensPassBothBudgets()
    {
        // Arrange
        var crew = new AgentExecutionBudget { MaxTokensConsumed = 500 };
        crew.RecordTokens(450);
        var child = crew.CreateChildBudget();

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(() => crew.RecordChildTokens(child, 200));

        // Assert: both budgets kept the tokens; the refusal is the crew's
        Assert.Equal("Token budget exhausted: 650 tokens sent and received over 2 calls (limit 500)", ex.Message);
        Assert.Equal(200, child.CurrentTokensConsumed);
    }

    // -- the context measure: contextTokens counts a turn's context once (#224) --

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldRecordTheGivenContextShare_WhenTheCallerComputedIt()
    {
        // Arrange
        var budget = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = int.MaxValue };

        // Act: a call resending a 10 000-token context whose conversation counts only 350 of it
        budget.RecordTokens(10_350, contextTokens: 350);

        // Assert: the sum pays the whole call, the context measure the share
        Assert.Equal(350, budget.CurrentContextTokens);
        Assert.Equal(10_350, budget.CurrentTokensConsumed);
        Assert.Equal(1, budget.CurrentTokenCalls);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldCountANegativeShareAsZero_WhenRecordingTokens()
    {
        var budget = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = int.MaxValue };

        budget.RecordTokens(100, contextTokens: -50);

        Assert.Equal(0, budget.CurrentContextTokens);
        Assert.Equal(100, budget.CurrentTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldCountTheWholeCall_WhenRecordedWithoutAShare()
    {
        var budget = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = int.MaxValue };

        budget.RecordTokens(400);

        Assert.Equal(400, budget.CurrentContextTokens);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldNameTheContextMeasure_WhenTheContextBudgetIsPassed()
    {
        // Arrange
        var budget = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = 1_000 };
        budget.RecordTokens(950, contextTokens: 950);

        // Act
        var ex = Assert.Throws<BudgetExhaustedException>(() => budget.RecordTokens(1_050, contextTokens: 150));

        // Assert: the dimension a script and the act's wrap-up read is the same as the sum's
        Assert.Equal(BudgetDimension.Tokens, ex.Dimension);
        Assert.Equal(
            "Token budget exhausted: 1100 context tokens (the growth or the uncached part of each prompt, plus every completion) over 2 calls (limit 1000 contextTokens)",
            ex.Message);
        Assert.Equal(1_100, budget.CurrentContextTokens);
        Assert.True(budget.IsExhausted);
        var gate = Assert.Throws<BudgetExhaustedException>(budget.ThrowIfExhausted);
        Assert.Equal(ex.Message, gate.Message);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldChargeTheSameShare_WhenTheBudgetChargesAnother()
    {
        // Arrange
        var crew = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = int.MaxValue };
        var run = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = int.MaxValue, ChargedTo = crew };

        // Act
        run.RecordTokens(10_010, contextTokens: 10_010);
        run.RecordTokens(10_110, contextTokens: 110);

        // Assert
        Assert.Equal(10_120, run.CurrentContextTokens);
        Assert.Equal(10_120, crew.CurrentContextTokens);
        Assert.Equal(20_120, crew.CurrentTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldLeaveTheChildWhatTheContextBudgetHasLeft_WhenCreatingAChildBudget()
    {
        var budget = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = 5_000 };
        budget.RecordTokens(1_200, contextTokens: 1_200);

        var child = budget.CreateChildBudget();

        Assert.Equal(3_800, child.MaxContextTokens);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldKeepTheChildUnbounded_WhenNoBudgetOfTheChainSetsContextTokens()
    {
        // Arrange: an unbounded chain that has already counted context
        var crew = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue };
        var run = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, ChargedTo = crew };
        run.RecordTokens(1_200, contextTokens: 1_200);

        // Act
        var child = run.CreateChildBudget();

        // Assert
        Assert.Equal(int.MaxValue, child.MaxContextTokens);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void ShouldCapTheChildByTheChargedBudget_WhenOnlyItSetsContextTokens()
    {
        var crew = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, MaxContextTokens = 5_000 };
        var run = new AgentExecutionBudget { MaxTokensConsumed = int.MaxValue, ChargedTo = crew };
        run.RecordTokens(1_200, contextTokens: 1_200);

        var child = run.CreateChildBudget();

        Assert.Equal(3_800, child.MaxContextTokens);
    }
}
