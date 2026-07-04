using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task.ValueObjects;
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ToolUsage = Orkeon.Domain.Tools.ToolUsage;

namespace Orkeon.Infrastructure.Tests.Process;

public class ConsensusTests
{
    #region Test Helpers

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = new();
        public IReadOnlyList<string> Messages => _messages;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        private class NoOpDisposable : IDisposable { public void Dispose() { } }
    }

    private static Agent CreateAgent(string role)
    {
        return new AgentBuilder()
            .Role(role)
            .Goal($"Goal for {role}")
            .Backstory($"Backstory for {role}")
            .Build();
    }

    private static DomainTask CreateTask(string name)
    {
        return new CrewTaskBuilder()
            .Description($"Description for {name}")
            .ExpectedOutput($"Expected output for {name}")
            .Build();
    }

    private static Crew CreateCrewWithAgentsAndTasks(Agent[] agents, DomainTask[] tasks)
    {
        var builder = new CrewBuilder()
            .Goal("Consensual crew")
            .Consensual();

        foreach (var agent in agents)
            builder.WithAgent(agent);
        foreach (var task in tasks)
            builder.WithTask(task);

        return builder.Build();
    }

    private static Vote MakeVote(string voterId, string choice, float confidence = 1.0f, float weight = 1.0f, string role = "worker")
    {
        return new Vote
        {
            VoterId = voterId,
            VoterRole = role,
            Choice = choice,
            Confidence = confidence,
            Weight = weight,
            Justification = $"Justification from {voterId}",
            Timestamp = DateTime.UtcNow
        };
    }

    #endregion

    #region MajorityVotingStrategy Tests

    [Fact]
    public async Task ShouldThreeVotesATwoVotesBAWins_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "B"), MakeVote("agent5", "B")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.Majority };

        var result = await strategy.TallyVotesAsync(votes, options);

        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(5, result.TotalVotes);
        Assert.Equal(3, result.VotesForWinner);
        Assert.Equal(60f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldSuperMajority60PercentNoConsensus_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "B"), MakeVote("agent5", "B")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.SuperMajority, ConsensusThreshold = 66.7f };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.False(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldSuperMajority80PercentConsensusReached_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A"),
            MakeVote("agent4", "A"), MakeVote("agent5", "B")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.SuperMajority, ConsensusThreshold = 66.7f };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(80f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldUnanimityOneDissentNoConsensus_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "B")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.Unanimity };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.False(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldUnanimityAllAgreeConsensusReached_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "A")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.Unanimity };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(100f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldPickFirst_WhenMajorityVotingTie()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "A"), MakeVote("agent2", "B") };
        var options = new VotingOptions { ConsensusType = ConsensusType.Majority };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.False(result.ConsensusReached);
        Assert.NotNull(result.WinningChoice);
        Assert.Equal(2, result.TotalVotes);
    }

    [Fact]
    public async Task ShouldEmptyVotesNoConsensus_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var result = await strategy.TallyVotesAsync(new List<Vote>(), new VotingOptions { ConsensusType = ConsensusType.Majority });
        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
        Assert.Equal(0, result.TotalVotes);
    }

    [Fact]
    public async Task ShouldWeightedVotesApplied_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A", confidence: 1.0f, weight: 3.0f),
            MakeVote("agent2", "B", confidence: 1.0f, weight: 1.0f)
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.Majority, UseWeightedVotes = true };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(75f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldWeightedVotesConfidenceMatters_WhenMajorityVoting()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A", confidence: 0.9f, weight: 1.0f),
            MakeVote("agent2", "A", confidence: 0.5f, weight: 1.0f)
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.Majority, UseWeightedVotes = true };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(100f, result.AgreementScore, 0.1f);
    }

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenMajorityVotingNullVotes()
    {
        var strategy = new MajorityVotingStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(() => strategy.TallyVotesAsync(null!, new VotingOptions()));
    }

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenMajorityVotingNullOptions()
    {
        var strategy = new MajorityVotingStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(() => strategy.TallyVotesAsync(new List<Vote>(), null!));
    }

    [Fact]
    public async Task ShouldSkipBlank_WhenMajorityVotingVotesWithBlankChoices()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "A"), MakeVote("agent2", ""), MakeVote("agent3", "A") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.Majority });
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldWinConsensus_WhenMajorityVotingSingleVote()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "A") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.Majority });
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(1, result.VotesForWinner);
    }

    #endregion

    #region WeightedConsensusStrategy Tests

    [Fact]
    public async Task ShouldSeniorVoteWeighsMore_WhenWeightedConsensus()
    {
        var roleWeights = new Dictionary<string, float> { ["senior"] = 3.0f, ["junior"] = 1.0f };
        var strategy = new WeightedConsensusStrategy(roleWeights);
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A", role: "senior"),
            MakeVote("agent2", "B", role: "junior"),
            MakeVote("agent3", "B", role: "junior")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.WeightedConsensus, ConsensusThreshold = 55f };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.True(result.AgreementScore > 55f);
    }

    [Fact]
    public async Task ShouldUnknownRoleGetsDefaultWeight_WhenWeightedConsensus()
    {
        var roleWeights = new Dictionary<string, float> { ["senior"] = 2.0f };
        var strategy = new WeightedConsensusStrategy(roleWeights);
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A", role: "unknown"),
            MakeVote("agent2", "A", role: "unknown")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.WeightedConsensus, ConsensusThreshold = 50f };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public void ShouldThrowArgumentNull_WhenWeightedConsensusNullRoleWeights()
    {
        Assert.Throws<ArgumentNullException>(() => new WeightedConsensusStrategy(null!));
    }

    #endregion

    #region BordaCountStrategy Tests

    [Fact]
    public async Task ShouldRankingProducesCorrectPoints_WhenBordaCount()
    {
        var strategy = new BordaCountStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A,B,C"), MakeVote("agent2", "A,C,B"), MakeVote("agent3", "B,A,C")
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.BordaCount };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(5f, result.Scores["A"]);
        Assert.Equal(3f, result.Scores["B"]);
        Assert.Equal(1f, result.Scores["C"]);
    }

    [Fact]
    public async Task ShouldAlwaysProducesWinner_WhenBordaCount()
    {
        var strategy = new BordaCountStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "X,Y"), MakeVote("agent2", "Y,X") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.BordaCount });
        Assert.True(result.ConsensusReached);
        Assert.NotNull(result.WinningChoice);
    }

    [Fact]
    public async Task ShouldWin_WhenBordaCountSingleChoice()
    {
        var strategy = new BordaCountStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "A"), MakeVote("agent2", "A") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.BordaCount });
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
    }

    [Fact]
    public async Task ShouldEmptyVotesNoConsensus_WhenBordaCount()
    {
        var strategy = new BordaCountStrategy();
        var result = await strategy.TallyVotesAsync(new List<Vote>(), new VotingOptions { ConsensusType = ConsensusType.BordaCount });
        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
    }

    [Fact]
    public async Task ShouldWeightedVotes_WhenBordaCount()
    {
        var strategy = new BordaCountStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A,B", weight: 2.0f),
            MakeVote("agent2", "B,A", weight: 1.0f)
        };
        var options = new VotingOptions { ConsensusType = ConsensusType.BordaCount, UseWeightedVotes = true };

        var result = await strategy.TallyVotesAsync(votes, options);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(2f, result.Scores["A"]);
        Assert.Equal(1f, result.Scores["B"]);
    }

    [Fact]
    public async Task ShouldCountFirstPlace_WhenBordaCountVotesForWinner()
    {
        var strategy = new BordaCountStrategy();
        var votes = new List<Vote>
        {
            MakeVote("agent1", "A,B,C"), MakeVote("agent2", "A,C,B"), MakeVote("agent3", "B,A,C")
        };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.BordaCount });
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(2, result.VotesForWinner);
    }

    #endregion

    #region VoteResult Metadata Tests

    [Fact]
    public async Task ShouldContainAllMetadata_WhenVoteResult()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "A"), MakeVote("agent2", "A"), MakeVote("agent3", "B") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.Majority });

        Assert.Equal(3, result.TotalVotes);
        Assert.Equal(2, result.VotesForWinner);
        Assert.True(result.ConsensusReached);
        Assert.Equal("A", result.WinningChoice);
        Assert.Equal(3, result.AllVotes.Count);
        Assert.Contains("A", result.Scores.Keys);
        Assert.Contains("B", result.Scores.Keys);
        Assert.True(result.AgreementScore > 0);
    }

    [Fact]
    public async Task ShouldScoresContainAllChoices_WhenVoteResult()
    {
        var strategy = new MajorityVotingStrategy();
        var votes = new List<Vote> { MakeVote("agent1", "A"), MakeVote("agent2", "B"), MakeVote("agent3", "C") };
        var result = await strategy.TallyVotesAsync(votes, new VotingOptions { ConsensusType = ConsensusType.Majority });

        Assert.Equal(3, result.Scores.Count);
        Assert.Contains("A", result.Scores.Keys);
        Assert.Contains("B", result.Scores.Keys);
        Assert.Contains("C", result.Scores.Keys);
    }

    #endregion

    #region ConsensualProcessStrategy Tests

    private (ConsensualProcessStrategy strategy, MockAgentExecutionService mockExec, Dictionary<AgentId, Agent> agents, Dictionary<TaskId, DomainTask> tasks) CreateProcessStrategy(
        ConsensualProcessOptions? opts = null)
    {
        var options = opts ?? new ConsensualProcessOptions
        {
            MaxVotingRounds = 3,
            EnableDiscussion = true,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity }
        };

        var agents = new Dictionary<AgentId, Agent>();
        var tasks = new Dictionary<TaskId, DomainTask>();

        var mockExec = new MockAgentExecutionService();
        var mockMemoryScope = new MockMemoryScope();
        var logger = new TestLogger<ConsensualProcessStrategy>();

        var taskRepo = new MockTaskRepository();
        var agentRepo = new MockAgentRepository();

        // We'll store agents/tasks directly and use the dictionaries for lookups
        // The MockTaskRepository and MockAgentRepository use internal dictionaries
        // but we need to add items via their public API

        var optionsWrapper = Options.Create(options);
        var votingStrategy = new MajorityVotingStrategy();

        var strategy = new ConsensualProcessStrategy(
            votingStrategy,
            mockExec,
            taskRepo,
            agentRepo,
            mockMemoryScope,
            logger,
            optionsWrapper);

        return (strategy, mockExec, agents, tasks);
    }

    /// <summary>
    /// Helper to create process strategy with repos that have agents/tasks pre-loaded.
    /// </summary>
    private (ConsensualProcessStrategy strategy, MockAgentExecutionService mockExec) CreateProcessStrategyWithData(
        ConsensualProcessOptions? opts,
        Agent[] agentsArray,
        DomainTask[] tasksArray)
    {
        var options = opts ?? new ConsensualProcessOptions
        {
            MaxVotingRounds = 3,
            EnableDiscussion = true,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity }
        };

        var mockExec = new MockAgentExecutionService();
        var mockMemoryScope = new MockMemoryScope();
        var logger = new TestLogger<ConsensualProcessStrategy>();

        var taskRepo = new MockTaskRepository();
        foreach (var t in tasksArray)
            taskRepo.AddTaskToStore(t);

        var agentRepo = new MockAgentRepository();
        foreach (var a in agentsArray)
            agentRepo.AddAgentToStore(a);

        var optionsWrapper = Options.Create(options);
        var votingStrategy = new MajorityVotingStrategy();

        var strategy = new ConsensualProcessStrategy(
            votingStrategy,
            mockExec,
            taskRepo,
            agentRepo,
            mockMemoryScope,
            logger,
            optionsWrapper);

        return (strategy, mockExec);
    }

    [Fact]
    public async Task ShouldUnanimousAgentsConsensusRound1_WhenConsensualProcess()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 3,
            EnableDiscussion = true,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("analyst1");
        var agent2 = CreateAgent("analyst2");
        var task1 = CreateTask("analyze");

        var (strategy, mockExec) = CreateProcessStrategyWithData(opts,
            new[] { agent1, agent2 }, new[] { task1 });

        var crew = CreateCrewWithAgentsAndTasks(new[] { agent1, agent2 }, new[] { task1 });

        var plan = ExecutionPlan.Create(crew.Tasks);
        var result = await strategy.ExecuteConsensualAsync(crew, plan);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldPropagateMeasuredTokenTelemetry_WhenConsensualProcess()
    {
        // Arrange — single agent reaches majority consensus in round 1; its single
        // execution costs 120 tokens (75 prompt / 45 completion).
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.Fail,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("metered-analyst");
        var task1 = CreateTask("metered-analyze");

        var (strategy, mockExec) = CreateProcessStrategyWithData(opts,
            new[] { agent1 }, new[] { task1 });

        mockExec.SetExecuteFunc((a, t, ctx, ct) =>
            new TaskResult(true, "voted output", null, [], TimeSpan.FromMilliseconds(10), TokensUsed: 120)
            {
                PromptTokens = 75,
                CompletionTokens = 45,
            });

        var crew = CreateCrewWithAgentsAndTasks(new[] { agent1 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        // Act
        var result = await strategy.ExecuteConsensualAsync(crew, plan, TestContext.Current.CancellationToken);

        // Assert — the voting round's measured cost reaches the crew metadata under the
        // canonical keys (R10.8); fails on the legacy code which returned metadata: null.
        Assert.True(result.Success);
        Assert.Equal(120, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
        Assert.Equal(75, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey));
        Assert.Equal(45, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey));
    }

    [Fact]
    public async Task ShouldNoConsensusFallbackAcceptBestScore_WhenConsensualProcess()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity }
        };

        var agent1 = CreateAgent("analyst1");
        var agent2 = CreateAgent("analyst2");
        var task1 = CreateTask("analyze");

        var (strategy, mockExec) = CreateProcessStrategyWithData(opts,
            new[] { agent1, agent2 }, new[] { task1 });

        var crew = CreateCrewWithAgentsAndTasks(new[] { agent1, agent2 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await strategy.ExecuteConsensualAsync(crew, plan);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldNoConsensusFallbackFail_WhenConsensualProcess()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.Fail,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity }
        };

        var agent1 = CreateAgent("analyst1");
        var agent2 = CreateAgent("analyst2");
        var task1 = CreateTask("analyze");

        var (strategy, mockExec) = CreateProcessStrategyWithData(opts,
            new[] { agent1, agent2 }, new[] { task1 });

        var crew = CreateCrewWithAgentsAndTasks(new[] { agent1, agent2 }, new[] { task1 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await strategy.ExecuteConsensualAsync(crew, plan);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("Consensus could not be reached", result.Error);
    }

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenConsensualProcessNullCrew()
    {
        var (strategy, _, _, _) = CreateProcessStrategy();
        var plan = ExecutionPlan.Create();
        await Assert.ThrowsAsync<ArgumentNullException>(() => strategy.ExecuteConsensualAsync(null!, plan));
    }

    [Fact]
    public async Task ShouldThrowArgumentNull_WhenConsensualProcessNullPlan()
    {
        var (strategy, _, _, _) = CreateProcessStrategy();
        var crew = new CrewBuilder().Goal("test").Consensual().Build();
        await Assert.ThrowsAsync<ArgumentNullException>(() => strategy.ExecuteConsensualAsync(crew, null!));
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenConsensualProcessNoAgents()
    {
        var task1 = CreateTask("task1");
        var taskRepo = new MockTaskRepository();
        taskRepo.AddTaskToStore(task1);

        var (strategy, _, _, _) = CreateProcessStrategy();

        var crew = new CrewBuilder().Goal("test").Consensual().Build();
        crew.AddTask(task1.TaskId);

        var plan = ExecutionPlan.Create(crew.Tasks);
        await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.ExecuteConsensualAsync(crew, plan));
    }

    [Fact]
    public async Task ShouldMultipleTasksAllExecuted_WhenConsensualProcess()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("analyst");
        var task1 = CreateTask("task1");
        var task2 = CreateTask("task2");

        var (strategy, mockExec) = CreateProcessStrategyWithData(opts,
            new[] { agent1 }, new[] { task1, task2 });

        var crew = CreateCrewWithAgentsAndTasks(new[] { agent1 }, new[] { task1, task2 });
        var plan = ExecutionPlan.Create(crew.Tasks);

        var result = await strategy.ExecuteConsensualAsync(crew, plan);

        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);
    }

    [Fact]
    public async Task ShouldMissingTaskSkipped_WhenConsensualProcess()
    {
        var opts = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.AcceptBestScore,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority }
        };

        var agent1 = CreateAgent("analyst");

        // Only add the agent to the repo, not the task
        var (strategy, _) = CreateProcessStrategyWithData(opts, new[] { agent1 }, Array.Empty<DomainTask>());

        var missingTaskId = TaskId.Create();
        var crew = new CrewBuilder().Goal("test").Consensual().Build();
        crew.AddAgent(agent1.AgentId);
        crew.AddTask(missingTaskId);

        var plan = ExecutionPlan.Create(crew.Tasks);
        var result = await strategy.ExecuteConsensualAsync(crew, plan);

        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
    }

    #endregion

    #region ConsensualProcessOptions Tests

    [Fact]
    public void ShouldDefaultValues_WhenConsensualProcessOptions()
    {
        var options = new ConsensualProcessOptions();
        Assert.Equal(3, options.MaxVotingRounds);
        Assert.True(options.EnableDiscussion);
        Assert.Equal(ConsensusFallback.AcceptBestScore, options.FallbackStrategy);
        Assert.NotNull(options.VotingOptions);
        Assert.NotNull(options.RoleWeights);
        Assert.Empty(options.RoleWeights);
    }

    [Fact]
    public void ShouldDefaultValues_WhenVotingOptions()
    {
        var options = new VotingOptions();
        Assert.Equal(ConsensusType.Majority, options.ConsensusType);
        Assert.Equal(50f, options.QuorumPercent);
        Assert.Equal(66.7f, options.ConsensusThreshold);
        Assert.False(options.UseWeightedVotes);
        Assert.Equal(3, options.MaxVotingRounds);
        Assert.True(options.AllowAbstention);
    }

    [Fact]
    public void ShouldDefaultValues_WhenVote()
    {
        var vote = new Vote();
        Assert.Equal(string.Empty, vote.VoterId);
        Assert.Equal(string.Empty, vote.VoterRole);
        Assert.Equal(string.Empty, vote.Choice);
        Assert.Equal(1.0f, vote.Confidence);
        Assert.Equal(1.0f, vote.Weight);
        Assert.Null(vote.Justification);
    }

    [Fact]
    public void ShouldDefaultValues_WhenVoteResult()
    {
        var result = new VoteResult();
        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
        Assert.Equal(0f, result.AgreementScore);
        Assert.Equal(0, result.TotalVotes);
        Assert.Equal(0, result.VotesForWinner);
        Assert.NotNull(result.Scores);
        Assert.Empty(result.Scores);
        Assert.NotNull(result.AllVotes);
        Assert.Empty(result.AllVotes);
    }

    #endregion

    #region DI Resolution Tests

    [Fact]
    public void ShouldResolveToMajorityVotingStrategy_WhenDIIVotingStrategy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVotingStrategy, MajorityVotingStrategy>();
        var provider = services.BuildServiceProvider();
        var strategy = provider.GetService<IVotingStrategy>();
        Assert.NotNull(strategy);
        Assert.IsType<MajorityVotingStrategy>(strategy);
    }

    [Fact]
    public void ShouldConsensualProcessOptionsConfiguredCorrectly_WhenDI()
    {
        var services = new ServiceCollection();
        services.Configure<ConsensualProcessOptions>(opt =>
        {
            opt.MaxVotingRounds = 5;
            opt.FallbackStrategy = ConsensusFallback.Fail;
        });
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ConsensualProcessOptions>>().Value;
        Assert.Equal(5, options.MaxVotingRounds);
        Assert.Equal(ConsensusFallback.Fail, options.FallbackStrategy);
    }

    #endregion
}
