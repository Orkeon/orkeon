using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Crew.Strategies;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// <see cref="OrkeonApplicationOptions.AgentSelectionStrategy"/>, asked at the level it is
/// promised: does setting it change who runs a task?
/// <para>
/// Before this, the answer was no. The option was bound, the two real strategies were
/// registered, <c>StrategyAgentSelectionService</c> resolved them, and every one of those
/// pieces had tests — while <c>IAgentSelectionService.SelectBestAgentAsync</c> had no caller
/// anywhere in <c>src/</c>. A feature can be complete in every part and absent as a whole; only
/// a test at the seam can tell.
/// </para>
/// </summary>
public sealed class TaskAgentSelectorTests
{
    private static DomainAgent Agent(string role) =>
        new AgentBuilder().Role(role).Goal("Execute tasks").Backstory("b").Build();

    private static CrewTask Task(string description) =>
        CrewTask.Create(TaskDescription.From(description), ExpectedOutput.From("out"));

    private static TaskAgentSelector Selector(
        AgentSelectionStrategyKind kind,
        IAgentSelectionService? service = null,
        IEmbeddingService? embedder = null) =>
        new(
            Options.Create(new OrkeonApplicationOptions { AgentSelectionStrategy = kind }),
            service,
            embedder);

    [Fact]
    public async Task FirstFit_keeps_round_robin()
    {
        var agents = new[] { Agent("a"), Agent("b") };
        var selector = Selector(AgentSelectionStrategyKind.FirstFit, new StubSelection(agents[1].Id), new StubEmbedder());

        Assert.Equal(agents[0].Id, (await selector.ForTaskAsync(Task("t0"), agents, 0, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(agents[1].Id, (await selector.ForTaskAsync(Task("t1"), agents, 1, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(agents[0].Id, (await selector.ForTaskAsync(Task("t2"), agents, 2, TestContext.Current.CancellationToken)).Id);
    }

    /// <summary>
    /// The discriminating case: the strategy names the agent round-robin would NOT have
    /// picked, so a passing assertion cannot be a lucky loop index.
    /// </summary>
    [Fact]
    public async Task A_configured_strategy_decides_who_runs_an_unassigned_task()
    {
        var agents = new[] { Agent("a"), Agent("b") };
        var service = new StubSelection(agents[1].Id);
        var selector = Selector(AgentSelectionStrategyKind.Embedding, service, new StubEmbedder());

        var chosen = await selector.ForTaskAsync(Task("t0"), agents, fallbackIndex: 0, TestContext.Current.CancellationToken);

        Assert.Equal(agents[1].Id, chosen.Id);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_declared_agent_is_never_overridden_by_a_strategy()
    {
        var agents = new[] { Agent("a"), Agent("b") };
        var service = new StubSelection(agents[1].Id);
        var selector = Selector(AgentSelectionStrategyKind.Skill, service, new StubEmbedder());

        var task = Task("t0");
        task.AssignTo(agents[0].Id);

        Assert.Equal(agents[0].Id, (await selector.ForTaskAsync(task, agents, 1, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task A_strategy_that_fails_degrades_to_round_robin()
    {
        var agents = new[] { Agent("a"), Agent("b") };
        var selector = Selector(AgentSelectionStrategyKind.Embedding, new ThrowingSelection(), new StubEmbedder());

        Assert.Equal(agents[1].Id, (await selector.ForTaskAsync(Task("t"), agents, fallbackIndex: 1, TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task A_strategy_naming_an_agent_the_crew_does_not_carry_degrades_to_round_robin()
    {
        var agents = new[] { Agent("a"), Agent("b") };
        var stranger = Agent("elsewhere");
        var selector = Selector(AgentSelectionStrategyKind.Embedding, new StubSelection(stranger.Id), new StubEmbedder());

        Assert.Equal(agents[0].Id, (await selector.ForTaskAsync(Task("t"), agents, fallbackIndex: 0, TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task No_embedding_service_means_round_robin_rather_than_a_crash()
    {
        var agents = new[] { Agent("a"), Agent("b") };
        var service = new StubSelection(agents[1].Id);
        var selector = Selector(AgentSelectionStrategyKind.Embedding, service, embedder: null);

        Assert.Equal(agents[0].Id, (await selector.ForTaskAsync(Task("t"), agents, fallbackIndex: 0, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task An_empty_roster_is_a_programming_error()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await TaskAgentSelector.RoundRobin.ForTaskAsync(Task("t"), [], 0, TestContext.Current.CancellationToken));
    }

    private sealed class StubSelection : IAgentSelectionService
    {
        private readonly AgentId _pick;

        public StubSelection(AgentId pick) => _pick = pick;

        public int Calls { get; private set; }

        public System.Threading.Tasks.Task<AgentSelectionResult> SelectBestAgentAsync(
            IReadOnlyList<DomainAgent> agents, CrewTask task, IEmbeddingService embedder, CancellationToken ct = default)
        {
            Calls++;
            return System.Threading.Tasks.Task.FromResult(AgentSelectionResult.Success(_pick, 0.9));
        }

        public System.Threading.Tasks.Task<AgentCapability> GetAgentCapabilityAsync(
            DomainAgent agent, IEmbeddingService embedder, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(AgentCapability.Create(agent.Role, agent.Goal, 1.0));

        public System.Threading.Tasks.Task<TaskRequirement> GetTaskRequirementAsync(
            CrewTask task, IEmbeddingService embedder, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(TaskRequirement.Create(
                task.Description.Value, RequirementType.Capability, task.ExpectedOutput.Value));
    }

    private sealed class ThrowingSelection : IAgentSelectionService
    {
        public System.Threading.Tasks.Task<AgentSelectionResult> SelectBestAgentAsync(
            IReadOnlyList<DomainAgent> agents, CrewTask task, IEmbeddingService embedder, CancellationToken ct = default) =>
            throw new InvalidOperationException("embedding endpoint unreachable");

        public System.Threading.Tasks.Task<AgentCapability> GetAgentCapabilityAsync(
            DomainAgent agent, IEmbeddingService embedder, CancellationToken ct = default) =>
            throw new InvalidOperationException("embedding endpoint unreachable");

        public System.Threading.Tasks.Task<TaskRequirement> GetTaskRequirementAsync(
            CrewTask task, IEmbeddingService embedder, CancellationToken ct = default) =>
            throw new InvalidOperationException("embedding endpoint unreachable");
    }

    private sealed class StubEmbedder : IEmbeddingService
    {
        private static readonly float[] s_vector = [1f, 0f, 0f];

        public System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text) =>
            System.Threading.Tasks.Task.FromResult(s_vector);
    }
}
