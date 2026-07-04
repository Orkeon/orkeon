using Orkeon.Application.Interfaces;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ToolUsage = Orkeon.Domain.Tools.ToolUsage;
using AppTaskResult = Orkeon.Application.Interfaces.Services.TaskResult;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using AgentId = Orkeon.Domain.Common.AgentId;
using AgentBuilder = Orkeon.Domain.Agent.AgentBuilder;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using CrewBuilder = Orkeon.Domain.Crew.CrewBuilder;
using CrewTaskBuilder = Orkeon.Domain.Task.CrewTaskBuilder;
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using IProcessStrategy = Orkeon.Domain.Crew.IProcessStrategy;
using AgentExecutionBudget = Orkeon.Domain.Autonomous.AgentExecutionBudget;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.CovConsensus;

/// <summary>
/// Complementary coverage tests for the Consensus zone, isolated by the CovConsensus_ prefix
/// and a dedicated namespace to avoid collisions with parallel test authors.
/// </summary>
public class CovConsensus_ConsensusCoverageTests
{
    #region Helpers

    private static DomainAgent CreateAgent(string role) => new AgentBuilder()
        .Role(role)
        .Goal($"Goal for {role}")
        .Backstory($"Backstory for {role}")
        .Build();

    private static DomainTask CreateTask(string name) => new CrewTaskBuilder()
        .Description($"Description for {name}")
        .ExpectedOutput($"Expected output for {name}")
        .Build();

    private static DomainCrew CreateCrew(DomainAgent[] agents, DomainTask[] tasks)
    {
        var builder = new CrewBuilder().Goal("Consensual crew").Consensual();
        foreach (var a in agents) builder.WithAgent(a);
        foreach (var t in tasks) builder.WithTask(t);
        return builder.Build();
    }

    private static Vote MakeVote(string voterId, string choice, float confidence = 1.0f, float weight = 1.0f, string role = "worker")
        => new()
        {
            VoterId = voterId,
            VoterRole = role,
            Choice = choice,
            Confidence = confidence,
            Weight = weight,
            Justification = $"From {voterId}",
            Timestamp = DateTime.UtcNow
        };

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope (no-op Dispose) is owned by the returned strategy, which lives for the duration of the test.")]
    private static (ConsensualProcessStrategy strategy, MockAgentExecutionService exec) BuildStrategy(
        ConsensualProcessOptions options,
        DomainAgent[] agents,
        DomainTask[] tasks,
        IVotingStrategy? voting = null,
        MockAgentExecutionService? exec = null)
    {
        var mockExec = exec ?? new MockAgentExecutionService();
        var taskRepo = new MockTaskRepository();
        foreach (var t in tasks) taskRepo.AddTaskToStore(t);
        var agentRepo = new MockAgentRepository();
        foreach (var a in agents) agentRepo.AddAgentToStore(a);

        var strategy = new ConsensualProcessStrategy(
            voting ?? new MajorityVotingStrategy(),
            mockExec,
            taskRepo,
            agentRepo,
            new MockMemoryScope(),
            NullLogger<ConsensualProcessStrategy>.Instance,
            Options.Create(options));

        return (strategy, mockExec);
    }

    #endregion

    #region ConsensualProcessStrategy — constructor guards

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ShouldThrowArgumentNull_WhenAnyDependencyIsNull(int nullIndex)
    {
        IVotingStrategy voting = new MajorityVotingStrategy();
        var exec = new MockAgentExecutionService();
        var taskRepo = new MockTaskRepository();
        var agentRepo = new MockAgentRepository();
        using var scope = new MockMemoryScope();
        ILogger<ConsensualProcessStrategy> logger = NullLogger<ConsensualProcessStrategy>.Instance;
        var options = Options.Create(new ConsensualProcessOptions());

        Assert.Throws<ArgumentNullException>(() => new ConsensualProcessStrategy(
            nullIndex == 0 ? null! : voting,
            nullIndex == 1 ? null! : exec,
            nullIndex == 2 ? null! : taskRepo,
            nullIndex == 3 ? null! : agentRepo,
            nullIndex == 4 ? null! : scope,
            nullIndex == 5 ? null! : logger,
            nullIndex == 6 ? null! : options));
    }

    #endregion

    #region ConsensualProcessStrategy — execution paths

    [Fact]
    public async Task ShouldEngageDiscussionRounds_ThenFallBack_WhenMultiAgentCannotReachUnanimity()
    {
        // Each agent votes for its own key, so with >1 agent Unanimity can never be reached.
        // This exercises the discussion-round branch (round < maxRounds && EnableDiscussion)
        // across multiple rounds, then the AcceptBestScore fallback.
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 3,
            EnableDiscussion = true,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity }
        };

        var agent1 = CreateAgent("alpha");
        var agent2 = CreateAgent("beta");
        var task1 = CreateTask("converge");

        var exec = new MockAgentExecutionService();
        bool sawDiscussionContext = false;
        exec.SetExecuteFunc((agent, task, ctx, ct) =>
        {
            if (ctx.Variables.ContainsKey("discussion_context"))
                sawDiscussionContext = true;
            return new AppTaskResult(true, $"out-{agent.Id}", null, Array.Empty<ToolUsage>(), TimeSpan.FromMilliseconds(5));
        });

        var (strategy, _) = BuildStrategy(opts, new[] { agent1, agent2 }, new[] { task1 }, exec: exec);
        var crew = CreateCrew(new[] { agent1, agent2 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await strategy.ExecuteConsensualAsync(crew, plan, TestContext.Current.CancellationToken);

        // AcceptBestScore fallback re-runs the first agent, so a result is produced.
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        // Discussion context must have been injected on rounds 2 and 3.
        Assert.True(sawDiscussionContext);
        // 2 agents x 3 rounds = 6, plus 1 fallback re-execution = 7 calls.
        Assert.Equal(7, exec.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task ShouldPropagateAgentException_WhenAgentThrowsDuringWhenAll()
    {
        // The exception thrown inside Task.Run surfaces through Task.WhenAll before the
        // per-task catch block, so it propagates out of the strategy.
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("good");
        var task1 = CreateTask("risky");

        var exec = new MockAgentExecutionService();
        exec.SetExecuteFunc((agent, task, ctx, ct) =>
            throw new InvalidOperationException("boom"));

        var (strategy, _) = BuildStrategy(opts, new[] { agent1 }, new[] { task1 }, exec: exec);
        var crew = CreateCrew(new[] { agent1 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteConsensualAsync(crew, plan, TestContext.Current.CancellationToken));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task ShouldApplyManagerDecisionFallback_WhenNoConsensus()
    {
        // ManagerDecision behaves like AcceptBestScore (re-executes first agent).
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.ManagerDecision,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity }
        };

        var agent1 = CreateAgent("a");
        var agent2 = CreateAgent("b");
        var task1 = CreateTask("decide");

        var exec = new MockAgentExecutionService();
        exec.SetExecuteFunc((agent, task, ctx, ct) =>
            new AppTaskResult(true, $"out-{agent.Id}", null, Array.Empty<ToolUsage>(), TimeSpan.FromMilliseconds(3)));

        var (strategy, _) = BuildStrategy(opts, new[] { agent1, agent2 }, new[] { task1 }, exec: exec);
        var crew = CreateCrew(new[] { agent1, agent2 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await strategy.ExecuteConsensualAsync(crew, plan, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        // 2 agents (round 1) + 1 fallback re-execution = 3 calls.
        Assert.Equal(3, exec.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task ShouldFallbackToCrewTasks_WhenPlanHasNoOrdering()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("solo");
        var task1 = CreateTask("only");

        var (strategy, _) = BuildStrategy(opts, new[] { agent1 }, new[] { task1 });
        var crew = CreateCrew(new[] { agent1 }, new[] { task1 });

        // Empty plan -> GetTasksInOrder() empty -> falls back to crew.Tasks.
        var emptyPlan = ExecutionPlan.Create();
        var result = await strategy.ExecuteConsensualAsync(crew, emptyPlan, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldSkipNullAgents_WhenLoadingCrewAgents()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("present");
        var task1 = CreateTask("t");

        // Only agent1 is stored in the repo, but the crew references a second (missing) agent.
        var (strategy, _) = BuildStrategy(opts, new[] { agent1 }, new[] { task1 });
        var crew = CreateCrew(new[] { agent1 }, new[] { task1 });
        crew.AddAgent(AgentId.Create()); // unknown id -> repo returns null -> skipped

        var plan = ExecutionPlan.Create(crew.Tasks);
        var result = await strategy.ExecuteConsensualAsync(crew, plan, TestContext.Current.CancellationToken);

        // Still succeeds because at least one agent resolved.
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ShouldThrowOperationCanceled_WhenCancelledBeforeTaskLoop()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("a");
        var task1 = CreateTask("t");
        var (strategy, _) = BuildStrategy(opts, new[] { agent1 }, new[] { task1 });
        var crew = CreateCrew(new[] { agent1 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => strategy.ExecuteConsensualAsync(crew, plan, cts.Token));
    }

    [Fact]
    public async Task ShouldExhaustMultipleRoundsWithoutDiscussion_ThenFail()
    {
        // EnableDiscussion = false means no discussion context is carried; multiple rounds
        // still execute but consensus is never reached -> Fail fallback returns failure.
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 2,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.Fail,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity }
        };

        var agent1 = CreateAgent("x");
        var agent2 = CreateAgent("y");
        var task1 = CreateTask("never");

        var exec = new MockAgentExecutionService();
        exec.SetExecuteFunc((agent, task, ctx, ct) =>
            new AppTaskResult(true, $"distinct-{agent.Id}", null, Array.Empty<ToolUsage>(), TimeSpan.FromMilliseconds(2)));

        var (strategy, _) = BuildStrategy(opts, new[] { agent1, agent2 }, new[] { task1 }, exec: exec);
        var crew = CreateCrew(new[] { agent1, agent2 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await strategy.ExecuteConsensualAsync(crew, plan, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Consensus could not be reached", result.Error);
        // 2 agents x 2 rounds = 4 executions.
        Assert.Equal(4, exec.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task ShouldUseWeightedStrategyInsideProcess()
    {
        var weighted = new WeightedConsensusStrategy(new Dictionary<string, float> { ["lead"] = 5.0f });
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("lead");
        var task1 = CreateTask("weighted");
        var (strategy, _) = BuildStrategy(opts, new[] { agent1 }, new[] { task1 }, voting: weighted);
        var crew = CreateCrew(new[] { agent1 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await strategy.ExecuteConsensualAsync(crew, plan, TestContext.Current.CancellationToken);
        Assert.True(result.Success);
    }

    #endregion

    #region ConsensualProcessStrategy — IProcessStrategy adapter (R3.3)

    [Fact]
    public void ShouldImplementIProcessStrategy_SoTheFactoryCanRouteConsensual()
    {
        var (strategy, _) = BuildStrategy(
            new ConsensualProcessOptions(), [CreateAgent("solo")], [CreateTask("t")]);

        Assert.IsType<IProcessStrategy>(strategy, exactMatch: false);
    }

    [Fact]
    public async Task ShouldRunConsensualPipeline_WhenExecuteSequentialAsyncEntryPointUsed()
    {
        // The IProcessStrategy sequential entry point must run the same voting pipeline
        // as ExecuteConsensualAsync (single agent → consensus on the first round).
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent = CreateAgent("solo");
        var task = CreateTask("t");

        var exec = new MockAgentExecutionService();
        exec.SetExecuteFunc((a, t, ctx, ct) =>
            new AppTaskResult(true, "agreed-output", null, Array.Empty<ToolUsage>(), TimeSpan.FromMilliseconds(5)));

        var (strategy, _) = BuildStrategy(opts, [agent], [task], exec: exec);
        IProcessStrategy processStrategy = strategy;
        var crew = CreateCrew([agent], [task]);
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await processStrategy.ExecuteSequentialAsync(
            crew, plan, inputVariables: null, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.Equal(1, exec.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task ShouldThrowNotSupported_WhenNonConsensualProcessEntryPointsUsed()
    {
        var (strategy, _) = BuildStrategy(
            new ConsensualProcessOptions(), [CreateAgent("solo")], [CreateTask("t")]);
        IProcessStrategy processStrategy = strategy;
        var crew = CreateCrew([CreateAgent("solo")], [CreateTask("t")]);
        var plan = ExecutionPlan.Create(crew.Tasks);

        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<NotSupportedException>(
            () => processStrategy.ExecuteHierarchicalAsync(crew, AgentId.Create(), null, ct));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => processStrategy.ExecuteParallelAsync(crew, plan, null, ct));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => processStrategy.ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, null, ct));
    }

    #endregion

    #region MajorityVotingStrategy — additional edge cases

    [Fact]
    public async Task ShouldReturnNoConsensus_WhenAllChoicesAreWhitespace()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("a", "   "), MakeVote("b", "") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.Majority }, TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
        Assert.Equal(2, result.TotalVotes);
        Assert.Empty(result.Scores);
    }

    [Fact]
    public async Task ShouldUseWeightedConsensusThreshold_WhenWeightedConsensusType()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("a", "A", weight: 4.0f),
            MakeVote("b", "B", weight: 1.0f)
        };
        var options = new VotingOptions
        {
            ConsensusType = ConsensusType.WeightedConsensus,
            ConsensusThreshold = 70f,
            UseWeightedVotes = true
        };

        var result = await strategy.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);
        Assert.True(result.ConsensusReached); // 80% > 70%
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(80f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldNotReachUnanimity_WhenMultipleDistinctChoices()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("a", "A"), MakeVote("b", "B") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.Unanimity }, TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Equal(2, result.Scores.Count);
    }

    [Fact]
    public async Task ShouldFallThroughToDefaultThreshold_WhenUnknownConsensusTypeViaWeighted()
    {
        // WeightedConsensusStrategy maps BordaCount -> WeightedConsensus, exercising that branch.
        var weighted = new WeightedConsensusStrategy(new Dictionary<string, float>());
        var votes = new List<Vote> { MakeVote("a", "A"), MakeVote("b", "A"), MakeVote("c", "B") };
        var options = new VotingOptions { ConsensusType = ConsensusType.BordaCount, ConsensusThreshold = 50f };

        var result = await weighted.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    #endregion

    #region WeightedConsensusStrategy — guards & passthrough

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenWeightedNullVotes()
    {
        var weighted = new WeightedConsensusStrategy(new Dictionary<string, float>());
        await Assert.ThrowsAsync<ArgumentNullException>(() => weighted.TallyVotesAsync(null!, new VotingOptions(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenWeightedNullOptions()
    {
        var weighted = new WeightedConsensusStrategy(new Dictionary<string, float>());
        await Assert.ThrowsAsync<ArgumentNullException>(() => weighted.TallyVotesAsync(new List<Vote>(), null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldPreserveNonBordaConsensusType_WhenWeighted()
    {
        var weighted = new WeightedConsensusStrategy(new Dictionary<string, float> { ["senior"] = 2f });
        var votes = new List<Vote>
        {
            MakeVote("a", "A", role: "senior"),
            MakeVote("b", "A", role: "junior"),
            MakeVote("c", "B", role: "junior")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.SuperMajority, ConsensusThreshold = 60f };

        var result = await weighted.TallyVotesAsync(votes, options, TestContext.Current.CancellationToken);
        // A weight = 2 (senior) + 1 (junior) = 3, B = 1 -> 75% > 60%.
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(75f, result.AgreementScore, 0.5f);
    }

    #endregion

    #region BordaCountStrategy — edge cases

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenBordaNullVotes()
    {
        var borda = new BordaCountStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(() => borda.TallyVotesAsync(null!, new VotingOptions(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenBordaNullOptions()
    {
        var borda = new BordaCountStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(() => borda.TallyVotesAsync(new List<Vote>(), null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnNoConsensus_WhenBordaAllChoicesEmptyStrings()
    {
        // All choices split to zero entries -> maxRankedItems == 0 -> no consensus.
        var borda = new BordaCountStrategy();
        var votes = new List<Vote> { MakeVote("a", ""), MakeVote("b", "   ") };
        var result = await borda.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.BordaCount }, TestContext.Current.CancellationToken);

        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
        Assert.Equal(2, result.TotalVotes);
    }

    [Fact]
    public async Task ShouldTrimEntries_WhenBordaRankingHasSpaces()
    {
        var borda = new BordaCountStrategy();
        var votes = new List<Vote> { MakeVote("a", " A , B , C "), MakeVote("b", " A , B , C ") };
        var result = await borda.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.BordaCount }, TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(4f, result.Scores["A"]); // (2 pts) x 2 voters
        Assert.Equal(2f, result.Scores["B"]);
        Assert.Equal(0f, result.Scores["C"]); // last place earns 0 points
    }

    [Fact]
    public async Task ShouldComputeAgreementScore_WhenBordaUnequalRankings()
    {
        var borda = new BordaCountStrategy();
        var votes = new List<Vote>
        {
            MakeVote("a", "A,B,C"),
            MakeVote("b", "A,B"),
            MakeVote("c", "A")
        };
        var result = await borda.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.BordaCount }, TestContext.Current.CancellationToken);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(3, result.VotesForWinner); // first place in all three rankings
        Assert.True(result.AgreementScore > 0f);
    }

    #endregion
}
