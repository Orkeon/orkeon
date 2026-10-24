using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Rag.Validation;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>Per-collection serialization of <see cref="DefaultIngestionPipeline"/> ingests.</summary>
public class DefaultIngestionPipelineConcurrencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Harness
    {
        private readonly RagIngestionOptions _options = new() { DefaultChunkingStrategy = "stub" };

        public FakeFileSystemService Fs { get; } = new FakeFileSystemService().AddMount("/kb").AddMount("/output");

        public GatedFakeDocumentStore Store { get; } = new();

        public InMemoryIngestionManifestStore Manifests { get; } = new();

        public DefaultIngestionPipeline Pipeline { get; }

        public Harness()
        {
            var chunking = new ChunkingStrategyFactory();
            chunking.Register("stub", () => new StubChunkingStrategy());
            Pipeline = new DefaultIngestionPipeline(
                new IngestionPipelineDependencies
                {
                    LoaderFactory = new DocumentLoaderFactory([new TextFileLoader(Fs)]),
                    ChunkingFactory = chunking,
                    EmbeddingProvider = new FakeEmbeddingProvider(),
                    Store = Store,
                    Validation = new DataValidationPipeline(
                        [new PromptInjectionDocumentValidator(), new ContentIntegrityValidator()],
                        new InMemoryQuarantineStore(),
                        new ProvenanceTracker()),
                    ManifestStore = Manifests,
                },
                _options);
        }

        public static IngestionRequest Request(string collection, string location) => new()
        {
            Collection = collection,
            Sources = [new SourceDescriptor { Location = location }],
            ChunkingStrategy = "stub",
            Chunking = new ChunkingOptions { MaxChunkSize = 10, Overlap = 0 },
        };
    }

    [Fact]
    [Trait("BR", "RT-28")]
    public async Task ShouldKeepAllChunksAndManifestEntries_WhenTwoIngestsRunConcurrentlyOnOneCollection()
    {
        var h = new Harness();
        h.Fs.AddFile("/kb/a.txt", "alpha");
        h.Fs.AddFile("/kb/b.txt", "bravo");

        var first = h.Pipeline.IngestAsync(Harness.Request("kb", "/kb/a.txt"), Ct);
        var second = h.Pipeline.IngestAsync(Harness.Request("kb", "/kb/b.txt"), Ct);

        // The first is blocked inside the store; the second must wait for the collection lock.
        Assert.Equal(1, h.Store.InsideOf("kb"));

        h.Store.Release();
        await Task.WhenAll(first, second);

        Assert.Equal(["alpha", "bravo"], h.Store.Chunks("kb").Select(c => c.Chunk.Content).Order().ToArray());
        var manifest = await h.Manifests.LoadAsync("kb", Ct);
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Sources.Count);
        Assert.Contains(manifest.Sources.Keys, k => k.EndsWith("a.txt", StringComparison.Ordinal));
        Assert.Contains(manifest.Sources.Keys, k => k.EndsWith("b.txt", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-28")]
    public async Task ShouldNotSerializeIngests_WhenCollectionsDiffer()
    {
        var h = new Harness();
        h.Fs.AddFile("/kb/a.txt", "alpha");
        h.Fs.AddFile("/kb/b.txt", "bravo");

        var first = h.Pipeline.IngestAsync(Harness.Request("kb-one", "/kb/a.txt"), Ct);
        var second = h.Pipeline.IngestAsync(Harness.Request("kb-two", "/kb/b.txt"), Ct);

        Assert.Equal(1, h.Store.InsideOf("kb-one"));
        Assert.Equal(1, h.Store.InsideOf("kb-two"));
        Assert.Equal(2, h.Store.TotalInside);

        h.Store.Release();
        await Task.WhenAll(first, second);
    }
}
