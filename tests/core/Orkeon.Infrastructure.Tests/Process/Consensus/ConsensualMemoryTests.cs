using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Agent;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;

namespace Orkeon.Infrastructure.Tests.Process.Consensus;

/// <summary>
/// GAP-20 — what a consensual crew remembers. Every agent answers and every agent casts a ballot,
/// all through the real <see cref="AgentExecutionService"/>, which stores a successful result in the
/// crew's memory. Only the answer the vote retained is a task result: it is stored once, under the
/// agent that wrote it — no candidate the vote rejected, no ballot.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope has a no-op Dispose.")]
public sealed partial class ConsensualMemoryTests : IDisposable
{
    private const string Preferred = "answer of beta";

    private readonly MockTaskRepository _tasks = new();
    private readonly MockAgentRepository _agents = new();
    private readonly StubExecutionOrchestrator _llm = new();
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly MemoryProviderFactory _providers;
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly MemoryService _memory;
    private readonly AgentExecutionService _execution;

    public ConsensualMemoryTests()
    {
        _providers = new MemoryProviderFactory(new FakeFileSystemService(), _httpClientFactory);
        _memory = new MemoryService(_providers, NullLogger<MemoryService>.Instance, _registry);
        _execution = new AgentExecutionService(
            NullLogger<AgentExecutionService>.Instance,
            _llm,
            new CallbackOrchestrator(NullLogger<CallbackOrchestrator>.Instance),
            new MemoryCoordinator(NullLogger<MemoryCoordinator>.Instance, _memory, _registry));

        // A candidate answers with its role; a voter puts "answer of beta" first when it is offered.
        _llm.Answer = (agent, task) => IsBallot(task)
            ? new TaskResult(true, BallotPreferring(task.Description.Value, Preferred), null, [], TimeSpan.Zero)
            : new TaskResult(true, $"answer of {agent.Role.Value}", null, [], TimeSpan.Zero);
    }

    public void Dispose()
    {
        _memory.Dispose();
        _providers.Dispose();
        _httpClientFactory.Dispose();
    }

    [Fact]
    public async Task Only_the_retained_answer_is_remembered_once_under_its_author()
    {
        var (alpha, beta, gamma) = (Agent("alpha"), Agent("beta"), Agent("gamma"));

        var output = await RunAsync(Options(ConsensusFallback.Fail), alpha, beta, gamma);

        Assert.True(output.Success, output.Error);
        Assert.Equal(Preferred, output.Output);
        var remembered = Assert.Single(await RememberedAsync());
        Assert.Equal(Preferred, remembered.Content);
        Assert.Equal(beta.Id, remembered.Metadata.CreatedBy);
    }

    [Fact]
    public async Task A_lone_successful_answer_is_remembered_once()
    {
        var (alpha, beta, gamma) = (Agent("alpha"), Agent("beta"), Agent("gamma"));
        _llm.Answer = (agent, task) => agent.Role.Value == "beta"
            ? new TaskResult(true, Preferred, null, [], TimeSpan.Zero)
            : new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, Error: "model unavailable");

        var output = await RunAsync(Options(ConsensusFallback.Fail), alpha, beta, gamma);

        Assert.True(output.Success, output.Error);
        var remembered = Assert.Single(await RememberedAsync());
        Assert.Equal(Preferred, remembered.Content);
        Assert.Equal(beta.Id, remembered.Metadata.CreatedBy);
    }

    [Fact]
    public async Task A_vote_without_consensus_under_Fail_leaves_nothing_to_remember()
    {
        // Every voter abstains: no candidate is retained, and none was a task result.
        _llm.Answer = (agent, task) => IsBallot(task)
            ? new TaskResult(true, """{"ranking": [], "abstain": true, "justification": "none is right"}""", null, [], TimeSpan.Zero)
            : new TaskResult(true, $"answer of {agent.Role.Value}", null, [], TimeSpan.Zero);

        var output = await RunAsync(Options(ConsensusFallback.Fail), Agent("alpha"), Agent("beta"), Agent("gamma"));

        Assert.False(output.Success);
        Assert.Empty(await RememberedAsync());
    }

    [Fact]
    public async Task The_answer_the_manager_chooses_is_remembered_under_the_agent_that_wrote_it()
    {
        // The peers abstain; the chair ranks "answer of gamma" first.
        var (alpha, beta, gamma, chair) = (Agent("alpha"), Agent("beta"), Agent("gamma"), Agent("chair"));
        _llm.Answer = (agent, task) => (IsBallot(task), agent.Role.Value) switch
        {
            (true, "chair") => new TaskResult(true, BallotPreferring(task.Description.Value, "answer of gamma"), null, [], TimeSpan.Zero),
            (true, _) => new TaskResult(true, """{"ranking": [], "abstain": true}""", null, [], TimeSpan.Zero),
            _ => new TaskResult(true, $"answer of {agent.Role.Value}", null, [], TimeSpan.Zero),
        };

        var output = await RunAsync(Options(ConsensusFallback.ManagerDecision), BuildCrew([alpha, beta, gamma], chair));

        Assert.True(output.Success, output.Error);
        var remembered = Assert.Single(await RememberedAsync());
        Assert.Equal("answer of gamma", remembered.Content);
        Assert.Equal(gamma.Id, remembered.Metadata.CreatedBy);
    }

    /// <summary>Everything the crew's memory store holds, whatever its text.</summary>
    private async Task<List<MemoryItem>> RememberedAsync() =>
        [.. await _providers.GetProvider("inmemory").SearchAsync(string.Empty, 100, cancellationToken: TestContext.Current.CancellationToken)];

    private static bool IsBallot(CrewTask task) =>
        task.Description.Value.StartsWith("You are casting a ballot", StringComparison.Ordinal);

    /// <summary>A JSON ballot that ranks the candidate whose answer is <paramref name="preferred"/> first.</summary>
    private static string BallotPreferring(string prompt, string preferred)
    {
        var candidates = CandidatePattern().Matches(prompt)
            .Select(m => (Label: m.Groups["label"].Value, Answer: m.Groups["answer"].Value.Trim()))
            .ToList();
        var ranking = candidates.OrderByDescending(c => c.Answer == preferred).Select(c => c.Label).ToList();
        return JsonSerializer.Serialize(new { ranking, abstain = false, confidence = 0.9, justification = "the clearest" });
    }

    [GeneratedRegex(@"### Candidate (?<label>\S+)\r?\n(?<answer>[^\r\n]*)")]
    private static partial Regex CandidatePattern();

    private static ConsensualProcessOptions Options(ConsensusFallback fallback) => new()
    {
        MaxVotingRounds = 1,
        EnableDiscussion = false,
        FallbackStrategy = fallback,
        VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Majority },
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
        var builder = new CrewBuilder().Name("review-board").Goal("Vote").Consensual().WithTask(task);
        foreach (var agent in agents)
            builder.WithAgent(agent);
        if (manager is not null)
            builder.WithManager(manager);
        return builder.Build();
    }

    private Task<DomainCrewOutput> RunAsync(ConsensualProcessOptions options, params DomainAgent[] agents) =>
        RunAsync(options, BuildCrew(agents));

    private Task<DomainCrewOutput> RunAsync(ConsensualProcessOptions options, Orkeon.Domain.Crew.Crew crew)
    {
        // What the orchestrator records at kickoff: the crew names the In-Memory provider.
        _registry.Record(crew.Id, "inmemory", crew.Name);

        var strategy = new ConsensualProcessStrategy(
            new VotingStrategyFactory(Microsoft.Extensions.Options.Options.Create(options)).Create(options.VotingOptions.ConsensusType),
            new AgentBallotCollector(_execution, NullLogger<AgentBallotCollector>.Instance),
            new CrewStrategyDependencies(_tasks, _agents, _execution, new MockMemoryScope()),
            new MemoryCoordinator(NullLogger<MemoryCoordinator>.Instance, _memory, _registry),
            NullLogger<ConsensualProcessStrategy>.Instance,
            Microsoft.Extensions.Options.Options.Create(options));

        return strategy.ExecuteConsensualAsync(crew, CrewExecutionPlan.Create(), ct: TestContext.Current.CancellationToken);
    }
}
