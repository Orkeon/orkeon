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
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// GAP-30 — what a hierarchical crew remembers: the output the manager accepted, once, under the
/// agent that wrote it. Every attempt runs through the real <see cref="AgentExecutionService"/>,
/// which stores a successful result unless the context says otherwise, into the real memory; the
/// manager is scripted. A rejected attempt used to be stored as well — all three, when the manager
/// rejected them all.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope has a no-op Dispose.")]
public sealed class HierarchicalMemoryTests : IDisposable
{
    private readonly MockTaskRepository _tasks = new();
    private readonly MockAgentRepository _agents = new();
    private readonly StubExecutionOrchestrator _llm = new();
    private readonly MockManagerAgent _manager = new();
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly MemoryProviderFactory _providers;
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly MemoryService _memory;
    private readonly MemoryCoordinator _coordinator;
    private readonly AgentExecutionService _execution;
    private int _attempts;

    public HierarchicalMemoryTests()
    {
        _providers = new MemoryProviderFactory(new FakeFileSystemService(), _httpClientFactory);
        _memory = new MemoryService(_providers, NullLogger<MemoryService>.Instance, _registry);
        var embedder = new MockEmbeddingProvider();
        _coordinator = new MemoryCoordinator(NullLogger<MemoryCoordinator>.Instance, _memory, _registry, embedder);
        _execution = new AgentExecutionService(
            NullLogger<AgentExecutionService>.Instance,
            _llm,
            new CallbackOrchestrator(NullLogger<CallbackOrchestrator>.Instance),
            _coordinator);

        // Each attempt of the worker answers a new draft.
        _llm.Answer = (_, _) => new TaskResult(true, $"draft {++_attempts}", null, [], TimeSpan.Zero);
    }

    public void Dispose()
    {
        _memory.Dispose();
        _providers.Dispose();
        _httpClientFactory.Dispose();
    }

    [Fact]
    public async Task A_rejection_then_an_acceptance_leave_one_memory_the_accepted_output_under_the_assigned_agent()
    {
        _manager.SetReviewResults(false, true);

        var (output, worker) = await RunAsync();

        Assert.True(output.Success, output.Error);
        var remembered = Assert.Single(await RememberedAsync());
        Assert.Equal("draft 2", remembered.Content);
        Assert.Equal(worker.Id, remembered.Metadata.CreatedBy);
    }

    [Fact]
    public async Task An_output_accepted_at_once_is_remembered_once()
    {
        var (output, _) = await RunAsync();

        Assert.True(output.Success, output.Error);
        Assert.Equal("draft 1", Assert.Single(await RememberedAsync()).Content);
    }

    [Fact]
    public async Task Three_rejections_leave_nothing_to_remember()
    {
        _manager.SetReviewResult(false);

        var (output, _) = await RunAsync();

        Assert.False(output.Success);
        Assert.Equal(3, _attempts);
        Assert.Empty(await RememberedAsync());
    }

    [Fact]
    public async Task A_worker_that_fails_leaves_nothing_to_remember()
    {
        _llm.Answer = (_, _) => new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, Error: "model unavailable");

        var (output, _) = await RunAsync();

        Assert.False(output.Success);
        Assert.Empty(await RememberedAsync());
    }

    [Fact]
    public async Task Every_attempt_recalls_and_none_stores_by_itself()
    {
        _manager.SetReviewResults(false, true);

        await RunAsync();

        Assert.Equal(2, _llm.Contexts.Count);
        Assert.All(_llm.Contexts, execution =>
        {
            Assert.False(execution.Context.StoreResultInMemory);
            Assert.True(execution.Context.RecallFromMemory);
        });
    }

    /// <summary>Everything the crew's memory store holds, whatever its text.</summary>
    private async Task<List<MemoryItem>> RememberedAsync() =>
        [.. await _providers.GetProvider("inmemory").SearchAsync(string.Empty, 100, cancellationToken: TestContext.Current.CancellationToken)];

    private DomainAgent Agent(string role)
    {
        var agent = new AgentBuilder().Role(role).Goal($"Goal of {role}").Backstory($"{role} works").Build();
        _agents.AddAgentToStore(agent);
        return agent;
    }

    private async Task<(DomainCrewOutput Output, DomainAgent Worker)> RunAsync()
    {
        var lead = Agent("Lead");
        var worker = Agent("Writer");
        var task = new CrewTaskBuilder().Description("Write the weekly editorial").ExpectedOutput("An editorial").Build();
        _tasks.AddTaskToStore(task);
        _manager.SetAssignResult(new TaskAssignment(task.Id, worker.Id, "the only writer", DateTime.UtcNow));
        var crew = new CrewBuilder().Name("editorial").Goal("Publish the editorial").Hierarchical(lead)
            .WithAgent(worker).WithTask(task).EnableMemory().WithMemoryProvider("inmemory").Build();

        // What the orchestrator records at kickoff: the crew remembers, in the In-Memory provider.
        _registry.Record(crew.Id, crew.MemoryProvider, crew.Name, crew.MemoryEnabled);

        var strategy = new HierarchicalProcessStrategy(
            _tasks, _agents, NullLogger<HierarchicalProcessStrategy>.Instance, _manager, _execution, new MockMemoryScope(), _coordinator);
        var output = await strategy.ExecuteHierarchicalAsync(crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken);
        return (output, worker);
    }
}
