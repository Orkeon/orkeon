using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Search;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Rag.Validation;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Search;

/// <summary>
/// Tests for <see cref="EphemeralCollectionSearchService"/> (RAG-03/C5): the
/// shared engine behind the search-tool façades. Key guarantees: a second
/// search over an unchanged corpus computes <b>zero corpus embeddings</b>
/// (only the query is embedded), the ephemeral collection name is
/// deterministic, and scored results flow through untouched, best first.
/// </summary>
public class EphemeralCollectionSearchServiceTests
{
    private sealed class Harness
    {
        public FakeEmbeddingProvider Embeddings { get; } = new();

        public FakeDocumentStore Store { get; } = new();

        public EphemeralCollectionSearchService CreateService()
        {
            var fs = new FakeFileSystemService().AddMount("/output");
            var options = new RagIngestionOptions();

            var pipeline = new DefaultIngestionPipeline(
                new DocumentLoaderFactory([new InlineTextLoader()]),
                ChunkingStrategyFactoryDefaults.CreateDefault(),
                Embeddings,
                Store,
                new DataValidationPipeline([], new InMemoryQuarantineStore(), new ProvenanceTracker()),
                new FileIngestionManifestStore(fs, options),
                options);

            return new EphemeralCollectionSearchService(pipeline, Store, Embeddings);
        }

        public static SourceDescriptor InlineSource(string location, string content) => new()
        {
            Location = location,
            Kind = InlineTextLoader.TextKind,
            Options = ImmutableDictionary<string, string>.Empty
                .Add(InlineTextLoader.ContentOptionKey, content),
        };

        public static EphemeralSearchRequest Request(
            string query, params SourceDescriptor[] sources) => new()
        {
            Sources = [.. sources],
            Query = query,
            TopK = 10,
            ChunkingStrategy = "recursive",
            Chunking = new ChunkingOptions { MaxChunkSize = 200, Overlap = 0 },
            CollectionPrefix = "test_tool",
        };
    }

    [Fact]
    public async Task SearchAsync_SecondRunOnUnchangedCorpus_ComputesZeroCorpusEmbeddings()
    {
        var harness = new Harness();
        var service = harness.CreateService();
        var request = Harness.Request(
            "anything",
            Harness.InlineSource("/docs/a.txt", "Alpha content about parsers."),
            Harness.InlineSource("/docs/b.txt", "Beta content about lexers."));

        var first = await service.SearchAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Ingestion.SourcesAdded);
        Assert.True(first.Ingestion.ChunksEmbedded > 0);
        var corpusBatchesAfterFirstRun = harness.Embeddings.BatchCalls.Count;
        Assert.True(corpusBatchesAfterFirstRun > 0);

        var second = await service.SearchAsync(request, TestContext.Current.CancellationToken);

        // Unchanged corpus: the manifest short-circuits every source — zero
        // chunk embeddings; the only new embedding call is the query itself.
        Assert.Equal(2, second.Ingestion.SourcesUnchanged);
        Assert.Equal(0, second.Ingestion.ChunksEmbedded);
        Assert.Equal(corpusBatchesAfterFirstRun, harness.Embeddings.BatchCalls.Count);
        Assert.Equal(2, harness.Embeddings.UnaryCalls.Count); // one query embedding per search
        Assert.NotEmpty(second.Results); // results still come from the persisted collection
    }

    [Fact]
    public async Task SearchAsync_ModifiedSource_ReingestsOnlyThatSource()
    {
        var harness = new Harness();
        var service = harness.CreateService();

        var stable = Harness.InlineSource("/docs/stable.txt", "Stable content.");
        await service.SearchAsync(
            Harness.Request("q", stable, Harness.InlineSource("/docs/volatile.txt", "Version one.")),
            TestContext.Current.CancellationToken);

        var second = await service.SearchAsync(
            Harness.Request("q", stable, Harness.InlineSource("/docs/volatile.txt", "Version two, changed.")),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, second.Ingestion.SourcesUnchanged);
        Assert.Equal(1, second.Ingestion.SourcesReingested);
    }

    [Fact]
    public async Task SearchAsync_SameCorpusAndOptions_YieldsTheSameCollection()
    {
        var harness = new Harness();
        var service = harness.CreateService();
        var sourceA = Harness.InlineSource("/docs/a.txt", "Some content.");
        var sourceB = Harness.InlineSource("/docs/b.txt", "Other content.");

        var first = await service.SearchAsync(
            Harness.Request("q", sourceA, sourceB), TestContext.Current.CancellationToken);

        // Same corpus, same options — source order must not matter.
        var second = await service.SearchAsync(
            Harness.Request("q", sourceB, sourceA), TestContext.Current.CancellationToken);

        Assert.Equal(first.Collection, second.Collection);
        Assert.StartsWith("eph-test-tool-", first.Collection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_DifferentCorpusOrOptions_YieldDifferentCollections()
    {
        var harness = new Harness();
        var service = harness.CreateService();
        var source = Harness.InlineSource("/docs/a.txt", "Some content.");

        var baseline = await service.SearchAsync(
            Harness.Request("q", source), TestContext.Current.CancellationToken);

        var otherSource = await service.SearchAsync(
            Harness.Request("q", Harness.InlineSource("/docs/other.txt", "Some content.")),
            TestContext.Current.CancellationToken);

        var otherChunking = await service.SearchAsync(
            Harness.Request("q", source) with { Chunking = new ChunkingOptions { MaxChunkSize = 999, Overlap = 0 } },
            TestContext.Current.CancellationToken);

        Assert.NotEqual(baseline.Collection, otherSource.Collection);
        Assert.NotEqual(baseline.Collection, otherChunking.Collection);
    }

    [Fact]
    public async Task SearchAsync_ContentChange_KeepsTheCollectionStable()
    {
        // Content is deliberately excluded from the collection identity: a
        // modified file re-ingests incrementally instead of forking a new
        // (fully re-embedded) collection.
        var harness = new Harness();
        var service = harness.CreateService();

        var v1 = await service.SearchAsync(
            Harness.Request("q", Harness.InlineSource("/docs/a.txt", "Version one.")),
            TestContext.Current.CancellationToken);
        var v2 = await service.SearchAsync(
            Harness.Request("q", Harness.InlineSource("/docs/a.txt", "Version two.")),
            TestContext.Current.CancellationToken);

        Assert.Equal(v1.Collection, v2.Collection);
    }

    [Fact]
    public async Task SearchAsync_ReturnsScoredResultsBestFirst_AndHonorsTopK()
    {
        var harness = new Harness();
        harness.Embeddings.EmbeddingFunc = text =>
            text.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                ? [1f, 0f, 0f, 0f]
                : [0f, 1f, 0f, 0f];
        var service = harness.CreateService();

        var request = Harness.Request(
            "alpha topic",
            Harness.InlineSource("/docs/a.txt", "This chunk is about alpha."),
            Harness.InlineSource("/docs/b.txt", "This chunk is about something else."),
            Harness.InlineSource("/docs/c.txt", "Another unrelated chunk."))
            with
        { TopK = 2 };

        var outcome = await service.SearchAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Results.Count);
        Assert.Contains("alpha", outcome.Results[0].Chunk.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(outcome.Results[0].Score >= outcome.Results[1].Score,
            "results must be ordered best first");
        Assert.Equal(1.0, outcome.Results[0].Score, 3);
    }

    [Fact]
    public async Task SearchAsync_EmptySources_ReturnsEmptyWithoutIngesting()
    {
        var harness = new Harness();
        var service = harness.CreateService();

        var outcome = await service.SearchAsync(
            new EphemeralSearchRequest { Sources = [], Query = "q" },
            TestContext.Current.CancellationToken);

        Assert.Empty(outcome.Results);
        Assert.Empty(harness.Embeddings.BatchCalls);
        Assert.Empty(harness.Embeddings.UnaryCalls);
        Assert.Empty(harness.Store.UpsertedCollections);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_Throws()
    {
        var service = new Harness().CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(
            new EphemeralSearchRequest
            {
                Sources = [Harness.InlineSource("/docs/a.txt", "x")],
                Query = "  ",
            },
            TestContext.Current.CancellationToken));
    }
}
