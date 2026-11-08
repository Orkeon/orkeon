using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.Memory;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Memory;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// FWK-04 — with a cognitive memory service the coordinator recalls by composite score and stores
/// through <c>RememberAsync</c>; without one the vector path is unchanged (MemoryCoordinatorTests).
/// </summary>
public sealed class MemoryCoordinatorCognitiveTests : IDisposable
{
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly FakeCognitiveMemoryService _cognitive = new();
    private readonly MockEmbeddingProvider _embedder = new();
    private readonly CrewId _crewId = CrewId.Create();
    private readonly DomainAgent _analyst = DomainAgent.Create(
        AgentRole.From("Analyst"), AgentGoal.From("Watch the news"), AgentBackstory.From("Reads a lot"));
    private readonly InMemoryProvider _store = new();
    private readonly MemoryService _inner;

    public MemoryCoordinatorCognitiveTests()
    {
        _inner = new MemoryService(new StubMemoryProviderFactory(_store), NullLogger<MemoryService>.Instance, _registry, _store);
        _registry.Record(_crewId, providerType: null, "news-desk", memoryEnabled: true);
    }

    public void Dispose() => _inner.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MemoryCoordinator Coordinator(CrewMemoryOptions? options = null) =>
        new(NullLogger<MemoryCoordinator>.Instance, _inner, _registry, _embedder,
            Options.Create(options ?? new CrewMemoryOptions()), _cognitive);

    private SimpleExecutionContext Context() => new(_crewId, [], NullMemoryScope.Instance, []);

    private static DomainTask Task(string description) =>
        DomainTask.Create(TaskDescription.From(description), ExpectedOutput.From("A short answer."));

    private static ScoredMemory Scored(string content, float composite) => new()
    {
        Item = MemoryItem.Create(content),
        CompositeScore = composite,
    };

    [Fact]
    public async System.Threading.Tasks.Task Recall_goes_through_the_cognitive_service_with_the_composite_floor_and_the_limit()
    {
        _cognitive.Recalled.Add(Scored("first", 0.9f));
        _cognitive.Recalled.Add(Scored("second", 0.8f));

        var recalled = await Coordinator(new CrewMemoryOptions { RecallLimit = 1, MinCompositeScore = 0.42f, MinScore = 0.99f })
            .RecallAsync(_analyst, Task("Summarize the news"), Context(), Ct);

        Assert.Equal(["first"], recalled.Select(m => m.Content));
        var call = Assert.Single(_cognitive.Recalls);
        Assert.Equal(_crewId, call.Crew);
        Assert.Equal(0.42f, call.Options!.MinScore);
        Assert.Equal(0, _embedder.GetEmbeddingCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Recall_keeps_the_character_budget()
    {
        _cognitive.Recalled.Add(Scored(new string('x', 500), 0.9f));

        var recalled = await Coordinator(new CrewMemoryOptions { MaxChars = 100 })
            .RecallAsync(_analyst, Task("Summarize the news"), Context(), Ct);

        Assert.Equal(100, Assert.Single(recalled).Content.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_recall_that_fails_recalls_nothing()
    {
        _cognitive.RecallFails = new InvalidOperationException("boom");

        var recalled = await Coordinator().RecallAsync(_analyst, Task("Summarize the news"), Context(), Ct);

        Assert.Empty(recalled);
    }

    [Fact]
    public async System.Threading.Tasks.Task Storing_goes_through_RememberAsync_with_the_task_as_context()
    {
        await Coordinator().StoreTaskResultAsync(_analyst, Task("Summarize the news"), "Five bullets", Context(), Ct);

        var remembered = Assert.Single(_cognitive.Remembered);
        Assert.Equal(_crewId, remembered.Crew);
        Assert.Equal("Five bullets", remembered.Content);
        Assert.Equal("Summarize the news", remembered.Context);
        Assert.Equal(0, _embedder.GetEmbeddingCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_crew_without_memory_never_reaches_the_cognitive_service()
    {
        _registry.Record(_crewId, providerType: null, "news-desk", memoryEnabled: false);
        var coordinator = Coordinator(new CrewMemoryOptions { ConsolidateOnRunEnd = true });

        await coordinator.StoreTaskResultAsync(_analyst, Task("t"), "out", Context(), Ct);
        await coordinator.RecallAsync(_analyst, Task("t"), Context(), Ct);
        await coordinator.FinishRunAsync(_crewId, Ct);

        Assert.Empty(_cognitive.Remembered);
        Assert.Empty(_cognitive.Recalls);
        Assert.Empty(_cognitive.Consolidated);
    }

    [Fact]
    public async System.Threading.Tasks.Task Consolidation_happens_at_the_end_of_the_run_under_the_setting_only()
    {
        var off = Coordinator(new CrewMemoryOptions { ConsolidateOnRunEnd = false });
        await off.StoreTaskResultAsync(_analyst, Task("t"), "out", Context(), Ct);
        await off.FinishRunAsync(_crewId, Ct);
        Assert.Empty(_cognitive.Consolidated);

        var on = Coordinator(new CrewMemoryOptions { ConsolidateOnRunEnd = true });
        await on.StoreTaskResultAsync(_analyst, Task("t"), "out", Context(), Ct);
        Assert.Empty(_cognitive.Consolidated);
        await on.FinishRunAsync(_crewId, Ct);
        Assert.Equal([_crewId], _cognitive.Consolidated);
    }
}
