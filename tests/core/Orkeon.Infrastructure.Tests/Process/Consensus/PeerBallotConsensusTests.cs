using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;

namespace Orkeon.Infrastructure.Tests.Process.Consensus;

/// <summary>
/// GAP-04 — the consensual vote weighs the answers. Every agent ranks the other agents'
/// successful answers, anonymised under labels; a failed execution is never a candidate;
/// the fallbacks re-run nothing; quorum and abstention change the count; the crew's input
/// variables reach every execution.
/// </summary>
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope has a no-op Dispose.")]
public sealed class PeerBallotConsensusTests
{
    private readonly MockAgentExecutionService _execution = new();
    private readonly MockTaskRepository _tasks = new();
    private readonly MockAgentRepository _agents = new();
    private readonly FakeBallotCollector _ballots = new();
    private readonly MockMemoryCoordinator _memory = new();
    private IMemoryCoordinator? _coordinator;
    private readonly ConcurrentQueue<SimpleExecutionContext> _contexts = new();
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);

    public PeerBallotConsensusTests()
    {
        // Each agent answers with its role; a role listed in _failures fails.
        _execution.SetExecuteFunc((agent, _, context, _) =>
        {
            _contexts.Enqueue(context);
            return _failures.TryGetValue(agent.Role.Value, out var error)
                ? new TaskResult(false, "BROKEN", null, [], TimeSpan.Zero, Error: error)
                : new TaskResult(true, Answer(agent.Role.Value), null, [], TimeSpan.Zero);
        });
    }

    [Theory]
    [InlineData(ConsensusType.Majority)]
    [InlineData(ConsensusType.SuperMajority)]
    [InlineData(ConsensusType.Unanimity)]
    public async Task Three_agents_that_agree_reach_majority_in_round_one(ConsensusType type)
    {
        _execution.SetExecuteFunc((_, _, _, _) => new TaskResult(true, "The tide turns at noon.", null, [], TimeSpan.Zero));
        var options = Options(type, rounds: 3, fallback: ConsensusFallback.Fail);

        var output = await RunAsync(options, Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.True(output.Success, output.Error);
        Assert.Equal("The tide turns at noon.", output.Output);
        Assert.Equal(3, _execution.ExecuteTaskCallCount);
        Assert.Equal(3, _ballots.Requests.Count);
    }

    [Fact]
    public async Task The_candidates_and_the_ballots_store_nothing_and_the_retained_answer_is_stored_once_under_its_author()
    {
        // GAP-20: a candidate and a ballot are not the task's result.
        var (alpha, beta, gamma) = (Agent("alpha"), Agent("beta"), Agent("gamma"));
        _ballots.Vote = request => FakeBallotCollector.Prefer(request, Answer("beta"));

        var output = await RunAsync(Options(ConsensusType.Majority), alpha, beta, gamma);

        Assert.True(output.Success, output.Error);
        Assert.Equal(3, _contexts.Count);
        Assert.All(_contexts, context => Assert.False(context.StoreResultInMemory));
        Assert.All(_ballots.Requests, request => Assert.False(request.Context.StoreResultInMemory));
        var stored = Assert.Single(_memory.Stored);
        Assert.Same(beta, stored.Agent);
        Assert.Equal(Answer("beta"), stored.Output);
    }

    [Fact]
    public async Task A_task_without_a_retained_answer_stores_nothing()
    {
        _failures["alpha"] = "down";
        _failures["beta"] = "down";
        _failures["gamma"] = "down";

        var output = await RunAsync(Options(ConsensusType.Majority), Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.False(output.Success);
        Assert.Empty(_memory.Stored);
    }

    [Fact]
    public async Task A_retained_answer_the_memory_cannot_store_stays_the_task_result_with_a_warning()
    {
        // GAP-30: the retained answer is the deliverable, already paid for. A memory store that is
        // down is a warning of the coordinator, as in every other mode — it used to fail the task,
        // and so the crew and every task depending on it.
        var crew = BuildCrew([Agent("alpha"), Agent("beta"), Agent("gamma")]);
        var down = new MockMemoryProvider();
        down.SetStoreException(new InvalidOperationException("the memory store is down"));
        var registry = new Orkeon.Application.Memory.CrewMemoryProviderRegistry();
        registry.Record(crew.Id, providerType: null, "review-board", memoryEnabled: true);
        using var memory = new Orkeon.Application.Memory.MemoryService(
            new StubMemoryProviderFactory(down), NullLogger<Orkeon.Application.Memory.MemoryService>.Instance, registry, down);
        var warnings = new Orkeon.Infrastructure.Tests.TestDoubles.TestLogger<Orkeon.Application.Memory.MemoryCoordinator>();
        _coordinator = new Orkeon.Application.Memory.MemoryCoordinator(warnings, memory, registry, new MockEmbeddingProvider());
        _ballots.Vote = request => FakeBallotCollector.Prefer(request, Answer("beta"));

        var output = await RunAsync(Options(ConsensusType.Majority), crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal(Answer("beta"), output.Output);
        Assert.True(warnings.HasLoggedWarning("the memory store is down"), string.Join(Environment.NewLine, warnings.LoggedMessages));
    }

    [Fact]
    public async Task Voters_rank_the_other_answers_anonymised_never_their_own()
    {
        await RunAsync(Options(ConsensusType.Majority), Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.Equal(3, _ballots.Requests.Count);
        foreach (var request in _ballots.Requests)
        {
            Assert.Equal(2, request.Candidates.Count);
            Assert.DoesNotContain(request.Candidates, c => c.Output == Answer(request.Voter.Role.Value));
            Assert.All(request.Candidates, c => Assert.Matches("^[A-Z]$", c.Label));
        }

        // Every answer is offered under one label to every voter who may rank it.
        var labels = _ballots.Requests.SelectMany(r => r.Candidates)
            .GroupBy(c => c.Output).ToDictionary(g => g.Key, g => g.Select(c => c.Label).Distinct().Single());
        Assert.Equal(3, labels.Values.Distinct().Count());
    }

    [Fact]
    public async Task A_collector_that_names_the_voters_own_answer_does_not_give_it_the_vote()
    {
        // A collector that puts the voter's own label first (the one of A, B, C it was not
        // offered). Counted, each agent would vote for itself: a three-way tie, no consensus.
        // Dropped, each vote goes to the voter's next choice and one answer carries.
        _ballots.Vote = request =>
        {
            var own = ThreeLabels.Except(request.Candidates.Select(c => c.Label)).Single();
            return new Ballot { Ranking = [own, .. request.Candidates.Select(c => c.Label)] };
        };

        var output = await RunAsync(
            Options(ConsensusType.Majority, fallback: ConsensusFallback.Fail), Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.True(output.Success, output.Error);
    }

    [Fact]
    public async Task Borda_ranks_the_answers_not_the_agent_order()
    {
        _ballots.Vote = FakeBallotCollector.RankBy(Quality);
        var options = Options(ConsensusType.BordaCount, rounds: 1, fallback: ConsensusFallback.Fail);
        var weak = Agent("quality-1");
        var strong = Agent("quality-3");
        var middle = Agent("quality-2");

        DomainAgent[][] orders = [[weak, strong, middle], [strong, middle, weak], [middle, weak, strong]];
        foreach (var order in orders)
        {
            var output = await RunAsync(options, order);

            Assert.True(output.Success, output.Error);
            Assert.Equal(Answer("quality-3"), output.Output);
        }
    }

    [Fact]
    public async Task A_failed_execution_is_not_a_candidate()
    {
        // BordaCount used to hand the task to the first agent, whether or not it had failed.
        var options = Options(ConsensusType.BordaCount, rounds: 1, fallback: ConsensusFallback.Fail);
        _failures["broken"] = "model unreachable";

        var output = await RunAsync(options, Agent("broken"), Agent("alpha"), Agent("beta"));

        Assert.True(output.Success, output.Error);
        Assert.NotEqual("BROKEN", output.Output);
        Assert.DoesNotContain(_ballots.Requests.SelectMany(r => r.Candidates), c => c.Output == "BROKEN");
        // The failed agent still votes, on both answers.
        Assert.Equal(2, Assert.Single(_ballots.Requests, r => r.Voter.Role.Value == "broken").Candidates.Count);
    }

    [Fact]
    public async Task A_lone_successful_answer_is_kept_without_a_ballot()
    {
        _failures["alpha"] = "timeout";
        _failures["beta"] = "timeout";

        var output = await RunAsync(Options(ConsensusType.Majority), Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(Answer("gamma"), output.Output);
        Assert.Empty(_ballots.Requests);
    }

    [Fact]
    public async Task A_task_every_agent_failed_fails_with_their_error_and_holds_no_ballot()
    {
        _failures["alpha"] = "quota exceeded";
        _failures["beta"] = "quota exceeded";

        var output = await RunAsync(
            Options(ConsensusType.Majority, rounds: 2, fallback: ConsensusFallback.AcceptBestScore),
            Agent("alpha"), Agent("beta"));

        Assert.False(output.Success);
        Assert.Contains("quota exceeded", output.Error, StringComparison.Ordinal);
        Assert.Empty(_ballots.Requests);
    }

    [Fact]
    public async Task AcceptBestScore_keeps_the_best_candidate_of_the_last_round_without_rerunning()
    {
        // Three voters prefer the strongest answer; one contrarian prefers the weakest: no
        // unanimity, but the strongest answer leads the count.
        _ballots.Vote = request => request.Voter.Role.Value == "quality-2b"
            ? FakeBallotCollector.RankBy(q => -Quality(q))(request)
            : FakeBallotCollector.RankBy(Quality)(request);
        var options = Options(ConsensusType.Unanimity, rounds: 2, fallback: ConsensusFallback.AcceptBestScore);

        var output = await RunAsync(
            options, Agent("quality-1"), Agent("quality-2b"), Agent("quality-3"), Agent("quality-2"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(Answer("quality-3"), output.Output);
        // 4 agents x 2 rounds, and no re-execution.
        Assert.Equal(8, _execution.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task ManagerDecision_lets_the_crew_manager_choose_among_the_last_round_answers()
    {
        var chair = Agent("chair");
        // The peers all abstain: no consensus. The manager ranks the answers on quality.
        _ballots.Vote = request => request.Voter.Id == chair.Id
            ? FakeBallotCollector.RankBy(Quality)(request)
            : Ballot.Abstention("undecided");
        var options = Options(ConsensusType.Majority, rounds: 1, fallback: ConsensusFallback.ManagerDecision);
        var crew = BuildCrew([Agent("quality-1"), Agent("quality-3"), Agent("quality-2"), chair], chair);

        var output = await RunAsync(options, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal(Answer("quality-3"), output.Output);
        // The manager arbitrates: it neither answers nor votes among the peers.
        Assert.Equal(3, _execution.ExecuteTaskCallCount);
        Assert.Equal(3, Assert.Single(_ballots.Requests, r => r.Voter.Id == chair.Id).Candidates.Count);
        Assert.Equal(4, _ballots.Requests.Count);
    }

    [Fact]
    public async Task ManagerDecision_fails_the_task_when_the_manager_abstains()
    {
        var chair = Agent("chair");
        _ballots.Vote = _ => Ballot.Abstention("none is acceptable");
        var options = Options(ConsensusType.Majority, rounds: 1, fallback: ConsensusFallback.ManagerDecision);

        var output = await RunAsync(options, BuildCrew([Agent("alpha"), Agent("beta"), Agent("gamma"), chair], chair));

        Assert.False(output.Success);
        Assert.Contains("none is acceptable", output.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagerDecision_without_a_manager_is_refused_before_any_agent_runs()
    {
        var options = Options(ConsensusType.Majority, rounds: 1, fallback: ConsensusFallback.ManagerDecision);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(options, Agent("alpha"), Agent("beta")));

        Assert.Contains("ManagerDecision", error.Message, StringComparison.Ordinal);
        Assert.Contains("manager", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _execution.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task Ballots_run_under_the_crew_id()
    {
        var chair = Agent("chair");
        _ballots.Vote = request => request.Voter.Id == chair.Id
            ? FakeBallotCollector.InShownOrder(request)
            : Ballot.Abstention("undecided");
        var options = Options(ConsensusType.Majority, rounds: 1, fallback: ConsensusFallback.ManagerDecision);
        var crew = BuildCrew([Agent("alpha"), Agent("beta"), chair], chair);

        await RunAsync(options, crew);

        Assert.NotEmpty(_ballots.Requests);
        Assert.All(_ballots.Requests, r => Assert.Equal(crew.Id, r.Context.CrewId));
        Assert.All(_contexts, c => Assert.Equal(crew.Id, c.CrewId));
    }

    [Theory]
    [InlineData(50f, false)]
    [InlineData(30f, true)]
    public async Task Quorum_not_met_is_no_consensus(float quorum, bool reached)
    {
        // One ballot out of three is expressed: 33 % of the voters.
        _ballots.Vote = request => request.Voter.Role.Value == "alpha"
            ? FakeBallotCollector.InShownOrder(request)
            : Ballot.Abstention("undecided");
        var options = Options(ConsensusType.Majority, rounds: 1, fallback: ConsensusFallback.Fail);
        options.VotingOptions.QuorumPercent = quorum;

        var output = await RunAsync(options, Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.Equal(reached, output.Success);
        if (!reached)
            Assert.Contains("Consensus could not be reached", output.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Abstention_refused_counts_as_a_vote_against_every_candidate(bool allowAbstention, bool reached)
    {
        // x and y rank w first, z abstains, w ranks x first: unanimous for w among the
        // expressed ballots, not once z's abstention counts against it.
        _ballots.Vote = request => request.Voter.Role.Value switch
        {
            "z" => Ballot.Abstention("undecided"),
            "w" => FakeBallotCollector.Prefer(request, Answer("x")),
            _ => FakeBallotCollector.Prefer(request, Answer("w")),
        };
        var options = Options(ConsensusType.Unanimity, rounds: 1, fallback: ConsensusFallback.Fail);
        options.VotingOptions.AllowAbstention = allowAbstention;

        var output = await RunAsync(options, Agent("w"), Agent("x"), Agent("y"), Agent("z"));

        Assert.Equal(reached, output.Success);
        if (reached)
            Assert.Equal(Answer("w"), output.Output);
    }

    [Fact]
    public async Task An_unreadable_ballot_is_an_abstention_and_does_not_fail_the_task()
    {
        _ballots.Vote = request => request.Voter.Role.Value == "gamma"
            ? throw new InvalidOperationException("provider returned HTML")
            : FakeBallotCollector.InShownOrder(request);

        var output = await RunAsync(
            Options(ConsensusType.Majority, fallback: ConsensusFallback.AcceptBestScore),
            Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(3, _ballots.Requests.Count);
    }

    [Fact]
    public async Task Input_variables_reach_every_agent_execution()
    {
        // Round 1: every voter abstains, so a discussion round follows; round 2 agrees.
        var calls = 0;
        _ballots.Vote = request => Interlocked.Increment(ref calls) <= 3
            ? Ballot.Abstention("undecided")
            : FakeBallotCollector.InShownOrder(request);
        var options = Options(ConsensusType.Majority, rounds: 2, fallback: ConsensusFallback.Fail);
        var agents = new[] { Agent("alpha"), Agent("beta"), Agent("gamma") };
        var crew = BuildCrew(agents);
        var strategy = Strategy(options);

        var output = await strategy.ExecuteSequentialAsync(
            crew, CrewExecutionPlan.Create(), new Dictionary<string, string> { ["topic"] = "tides" },
            TestContext.Current.CancellationToken);

        Assert.True(output.Success, output.Error);
        Assert.Equal(6, _contexts.Count);
        Assert.All(_contexts, c => Assert.Equal("tides", c.Variables["topic"]));
        Assert.Equal(3, _contexts.Count(c => c.Variables.ContainsKey("discussion_context")));
        Assert.All(_ballots.Requests, r => Assert.Equal("tides", r.Context.Variables["topic"]));
    }

    private static readonly string[] ThreeLabels = ["A", "B", "C"];

    private static string Answer(string role) => $"answer of {role}";

    /// <summary>The quality digit a "quality-N" role carries in its answer.</summary>
    private static int Quality(string answer) =>
        answer.LastIndexOf("quality-", StringComparison.Ordinal) is var i and >= 0
            ? answer[i + "quality-".Length] - '0'
            : 0;

    private static ConsensualProcessOptions Options(
        ConsensusType type, int rounds = 1, ConsensusFallback fallback = ConsensusFallback.Fail) => new()
    {
        MaxVotingRounds = rounds,
        EnableDiscussion = true,
        FallbackStrategy = fallback,
        VotingOptions = new VotingOptions { ConsensusType = type },
    };

    private DomainAgent Agent(string role)
    {
        var agent = new AgentBuilder().Role(role).Goal($"Goal of {role}").Backstory($"{role} works").Build();
        _agents.AddAgentToStore(agent);
        return agent;
    }

    private Orkeon.Domain.Crew.Crew BuildCrew(DomainAgent[] agents, DomainAgent? manager = null)
    {
        var task = new CrewTaskBuilder().Description("When does the tide turn?").ExpectedOutput("A time").Build();
        _tasks.AddTaskToStore(task);
        var builder = new CrewBuilder().Goal("Vote").Consensual().WithTask(task);
        foreach (var agent in agents)
            builder.WithAgent(agent);
        if (manager is not null)
            builder.WithManager(manager);
        return builder.Build();
    }

    private ConsensualProcessStrategy Strategy(ConsensualProcessOptions options) => new(
        new VotingStrategyFactory(Microsoft.Extensions.Options.Options.Create(options)).Create(options.VotingOptions.ConsensusType),
        _ballots,
        new CrewStrategyDependencies(_tasks, _agents, _execution, new MockMemoryScope()),
        _coordinator ?? _memory,
        NullLogger<ConsensualProcessStrategy>.Instance,
        Microsoft.Extensions.Options.Options.Create(options));

    private Task<DomainCrewOutput> RunAsync(ConsensualProcessOptions options, params DomainAgent[] agents) =>
        RunAsync(options, BuildCrew(agents));

    private Task<DomainCrewOutput> RunAsync(ConsensualProcessOptions options, Orkeon.Domain.Crew.Crew crew) =>
        Strategy(options).ExecuteConsensualAsync(crew, CrewExecutionPlan.Create(), ct: TestContext.Current.CancellationToken);
}
