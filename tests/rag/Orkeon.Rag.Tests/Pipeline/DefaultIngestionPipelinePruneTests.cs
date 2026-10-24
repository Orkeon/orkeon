using Orkeon.Rag.Abstractions.Interfaces;
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

/// <summary>Pruning of absent manifest sources by <see cref="DefaultIngestionPipeline"/> (RT-23).</summary>
public class DefaultIngestionPipelinePruneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingDocumentStore : IDocumentStore
    {
        public List<string> DeletedSources { get; } = [];

        public string? FailDeleteOfSuffix { get; set; }

        public Task UpsertAsync(string collection, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ScoredChunk>> SearchAsync(string collection, RetrievalQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScoredChunk>>([]);

        public Task DeleteBySourceAsync(string collection, string sourceId, CancellationToken cancellationToken = default)
        {
            if (FailDeleteOfSuffix is not null && sourceId.EndsWith(FailDeleteOfSuffix, StringComparison.Ordinal))
            {
                throw new IOException("delete failed for " + sourceId);
            }

            DeletedSources.Add(sourceId);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public FakeFileSystemService Fs { get; } = new FakeFileSystemService().AddMount("/kb").AddMount("/output");

        public RecordingDocumentStore Store { get; } = new();

        public InMemoryIngestionManifestStore Manifests { get; } = new();

        public DefaultIngestionPipeline Pipeline { get; }

        public Harness()
        {
            Fs.AddFile("/kb/s1.txt", "alpha");
            Fs.AddFile("/kb/s2.txt", "bravo");
            Fs.AddFile("/kb/s3.txt", "charlie");
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
                new RagIngestionOptions { DefaultChunkingStrategy = "stub" });
        }

        public Task<IngestionReport> IngestAsync(bool prune, params string[] locations) =>
            Pipeline.IngestAsync(
                new IngestionRequest
                {
                    Collection = "kb",
                    Sources = [.. locations.Select(l => new SourceDescriptor { Location = l })],
                    ChunkingStrategy = "stub",
                    Chunking = new ChunkingOptions { MaxChunkSize = 10, Overlap = 0 },
                    Prune = prune,
                },
                Ct);

        public async Task<IngestionManifest> ManifestAsync() => (await Manifests.LoadAsync("kb", Ct))!;
    }

    [Fact]
    [Trait("BR", "RT-23")]
    public async Task ShouldDeleteChunksOfAbsentSources_WhenPruneRequested()
    {
        var h = new Harness();
        await h.IngestAsync(false, "/kb/s1.txt", "/kb/s2.txt");
        Assert.Equal(2, (await h.ManifestAsync()).Sources.Count);
        Assert.Empty(h.Store.DeletedSources);

        var report = await h.IngestAsync(true, "/kb/s1.txt");

        Assert.Equal(1, report.SourcesPruned);
        var deleted = Assert.Single(h.Store.DeletedSources);
        Assert.EndsWith("s2.txt", deleted, StringComparison.Ordinal);
        var kept = Assert.Single((await h.ManifestAsync()).Sources.Keys);
        Assert.EndsWith("s1.txt", kept, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-23")]
    public async Task ShouldKeepSourcesOutsideGlobSet_WhenPruneRequested()
    {
        var h = new Harness();
        await h.IngestAsync(false, "/kb/s1.txt", "/kb/s2.txt", "/kb/s3.txt");

        var withoutPrune = await h.IngestAsync(false, "/kb/s1.txt");
        Assert.Equal(0, withoutPrune.SourcesPruned);
        Assert.Empty(h.Store.DeletedSources);

        var report = await h.IngestAsync(true, "/kb/s1.txt", "/kb/s2.txt");

        Assert.Equal(1, report.SourcesPruned);
        var deleted = Assert.Single(h.Store.DeletedSources);
        Assert.EndsWith("s3.txt", deleted, StringComparison.Ordinal);
        Assert.Equal(2, (await h.ManifestAsync()).Sources.Count);
    }

    [Fact]
    [Trait("BR", "RT-23")]
    public async Task ShouldNotPruneSource_WhenItsLoadFailsInThisRun()
    {
        var h = new Harness();
        await h.IngestAsync(false, "/kb/s1.txt", "/kb/s2.txt");
        Assert.True(await h.Fs.DeleteAsync("/kb/s2.txt", false, Ct));

        var report = await h.IngestAsync(true, "/kb/s1.txt", "/kb/s2.txt");

        Assert.NotEmpty(report.Errors);
        Assert.Equal(0, report.SourcesPruned);
        Assert.Empty(h.Store.DeletedSources);
        Assert.Equal(2, (await h.ManifestAsync()).Sources.Count);
    }

    [Fact]
    [Trait("BR", "RT-23")]
    public async Task ShouldRecordErrorAndKeepGoing_WhenPruneDeleteFails()
    {
        var h = new Harness();
        await h.IngestAsync(false, "/kb/s1.txt", "/kb/s2.txt", "/kb/s3.txt");
        h.Store.FailDeleteOfSuffix = "s3.txt";

        var report = await h.IngestAsync(true, "/kb/s1.txt");

        Assert.Contains(report.Errors, e => e.Contains("s3.txt", StringComparison.Ordinal));
        Assert.Equal(1, report.SourcesPruned);
        var deleted = Assert.Single(h.Store.DeletedSources);
        Assert.EndsWith("s2.txt", deleted, StringComparison.Ordinal);
        var keys = (await h.ManifestAsync()).Sources.Keys.ToArray();
        Assert.Contains(keys, k => k.EndsWith("s1.txt", StringComparison.Ordinal));
        Assert.Contains(keys, k => k.EndsWith("s3.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(keys, k => k.EndsWith("s2.txt", StringComparison.Ordinal));
    }
}
