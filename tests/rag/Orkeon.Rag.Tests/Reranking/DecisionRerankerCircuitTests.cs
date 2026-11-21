using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Reranking;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Reranking;

/// <summary>
/// #433: with the decision circuit off - the switch, the settings or no provider - the
/// <c>decision</c> reranker asks no decision model, passes the passages through in their retrieval
/// order and warns once.
/// </summary>
public sealed class DecisionRerankerCircuitTests
{
    // Retrieval order is not score order: a reranker that sorted by score would give chunk-1, chunk-2, chunk-0.
    private static readonly ScoredChunk[] Passages = [Passage(0, 0.7), Passage(1, 0.9), Passage(2, 0.8)];

    private static ScoredChunk Passage(int index, double score) => new()
    {
        Chunk = new Chunk { Id = $"chunk-{index}", DocumentId = "doc", SourceId = "source", Content = $"content {index}", Index = index },
        Score = score,
        ScoreOrigin = "vector",
    };

    private sealed class Host : IDisposable
    {
        public Host(DecisionCircuit? circuit, bool withModel)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IChatClient>(_ => new FakeChatClient());
            services.AddSingleton<ILogger<DecisionModelReranker>>(Logger);
            if (circuit is not null)
                services.AddSingleton(circuit);
            if (withModel)
                services.AddSingleton<IDecisionModel>(Model);
            services.AddOrkeonRagReranking();
            Container = services.BuildServiceProvider();
        }

        public FakeDecisionModel Model { get; } = new();

        public RecordingLogger<DecisionModelReranker> Logger { get; } = new();

        public ServiceProvider Container { get; }

        public RerankerFactory Factory => Container.GetRequiredService<RerankerFactory>();

        public void Dispose() => Container.Dispose();
    }

    public static TheoryData<string, bool> OffCircuits => new()
    {
        { nameof(DecisionCircuitCause.Switch), true },
        { nameof(DecisionCircuitCause.Settings), true },
        { nameof(DecisionCircuitCause.NoProvider), false },
    };

    private static DecisionCircuit? Circuit(string cause) => cause == nameof(DecisionCircuitCause.NoProvider)
        ? null
        : DecisionCircuit.OffBy(Enum.Parse<DecisionCircuitCause>(cause));

    [Theory]
    [MemberData(nameof(OffCircuits))]
    [Trait("BR", "CH-433")]
    public async Task ShouldPassThePassagesThroughInOrder_WhenTheCircuitIsOff(string cause, bool withModel)
    {
        // Arrange
        using var host = new Host(Circuit(cause), withModel);
        var reranker = host.Factory.Create(DecisionModelReranker.RerankerName);

        // Act
        var result = await reranker.RerankAsync("what is X?", Passages, topN: 3, TestContext.Current.CancellationToken);

        // Assert: no decision reranker is built, so no row asks a model, the one without a model included.
        Assert.IsNotType<DecisionModelReranker>(reranker);
        Assert.Equal(["chunk-0", "chunk-1", "chunk-2"], result.Select(r => r.Chunk.Id));
        Assert.Empty(host.Model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldWarnOnceNamingTheCause_WhenTheDecisionRerankerIsCreatedTwiceWithTheCircuitOff()
    {
        // Arrange
        using var host = new Host(DecisionCircuit.Off, withModel: true);
        host.Factory.Create(DecisionModelReranker.RerankerName);

        // Act
        host.Factory.Create(DecisionModelReranker.RerankerName);

        // Assert
        var warning = Assert.Single(host.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("the decision circuit is off: --no-decision", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldBuildTheDecisionReranker_WhenTheCircuitIsOnWithAProvider()
    {
        // Arrange
        using var host = new Host(DecisionCircuit.On, withModel: true);

        // Act
        var reranker = host.Factory.Create(DecisionModelReranker.RerankerName);

        // Assert
        Assert.IsType<DecisionModelReranker>(reranker);
    }
}
