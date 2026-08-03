using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Retrieval;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Rag.Validation;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Ingestion;

/// <summary>
/// The ingestion manifest can outlive the data it describes: the manifest is
/// persisted through the virtual file system while the default document store is
/// backed by the host's in-memory <c>IMemoryProvider</c>. Measured 2026-08-03 —
/// two <c>orkeon run</c> processes over one collection gave 3 citations then 0,
/// because the second read the manifest, concluded "everything unchanged",
/// embedded nothing, and reported success against an EMPTY store.
/// </summary>
public class StaleManifestTests
{
    /// <summary>Store that can report emptiness — the capability under test.</summary>
    private sealed class ProbeableStore : IDocumentStore, IDocumentStoreCollectionProbe
    {
        public bool HasContent { get; set; }

        public int ProbeCalls { get; private set; }

        public List<string> UpsertedCollections { get; } = [];

        public Task UpsertAsync(
            string collection,
            IReadOnlyList<EmbeddedChunk> chunks,
            CancellationToken cancellationToken = default)
        {
            UpsertedCollections.Add(collection);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
            string collection,
            RetrievalQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScoredChunk>>([]);

        public Task DeleteBySourceAsync(
            string collection,
            string sourceId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> HasContentAsync(string collection, CancellationToken cancellationToken = default)
        {
            ProbeCalls++;
            return Task.FromResult(HasContent);
        }
    }

    /// <summary>Store WITHOUT the capability — must keep the previous behaviour.</summary>
    private sealed class OpaqueStore : IDocumentStore
    {
        public List<string> UpsertedCollections { get; } = [];

        public Task UpsertAsync(
            string collection,
            IReadOnlyList<EmbeddedChunk> chunks,
            CancellationToken cancellationToken = default)
        {
            UpsertedCollections.Add(collection);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
            string collection,
            RetrievalQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScoredChunk>>([]);

        public Task DeleteBySourceAsync(
            string collection,
            string sourceId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Second_ingestion_re_embeds_when_the_manifest_survived_but_the_store_is_empty()
    {
        var harness = new StaleManifestHarness();
        var store = new ProbeableStore { HasContent = false };

        // Pass 1: nothing known, everything ingested, store now "has content".
        var first = await harness.IngestAsync(store);
        Assert.Equal(1, first.SourcesAdded);
        Assert.True(first.ChunksCreated > 0);
        store.HasContent = true;

        // Pass 2 with a live store: the manifest is trusted, nothing re-embedded.
        var second = await harness.IngestAsync(store);
        Assert.Equal(1, second.SourcesUnchanged);
        Assert.Equal(0, second.ChunksCreated);

        // Pass 3 simulating a NEW PROCESS: manifest still on disk, store empty.
        store.HasContent = false;
        var third = await harness.IngestAsync(store);
        Assert.Equal(0, third.SourcesUnchanged);
        Assert.Equal(1, third.SourcesAdded);
        Assert.True(third.ChunksCreated > 0, "a stale manifest must not suppress re-embedding");
    }

    [Fact]
    public async Task A_store_that_cannot_be_probed_keeps_trusting_the_manifest()
    {
        // Durable stores cannot hit the mismatch, and forcing a full re-embed on
        // every run would be a serious regression for them.
        var harness = new StaleManifestHarness();
        var store = new OpaqueStore();

        await harness.IngestAsync(store);
        var second = await harness.IngestAsync(store);

        Assert.Equal(1, second.SourcesUnchanged);
        Assert.Equal(0, second.ChunksCreated);
    }

    [Fact]
    public async Task An_explicit_reindex_does_not_waste_a_probe()
    {
        var harness = new StaleManifestHarness();
        var store = new ProbeableStore { HasContent = true };

        await harness.IngestAsync(store);
        var before = store.ProbeCalls;
        await harness.IngestAsync(store, reindex: true);

        Assert.Equal(before, store.ProbeCalls);
    }

    [Fact]
    public async Task The_probe_is_skipped_on_a_first_ingestion_because_there_is_no_manifest_to_doubt()
    {
        var harness = new StaleManifestHarness();
        var store = new ProbeableStore { HasContent = false };

        await harness.IngestAsync(store);

        Assert.Equal(0, store.ProbeCalls);
    }

    [Fact]
    public async Task The_hybrid_decorator_forwards_the_probe_instead_of_swallowing_it()
    {
        // The decorator ALWAYS wraps the real store (ingestion has to feed the
        // lexical index), so a swallowed capability would disable the fix in every
        // default host — which is what happened the first time it was added.
        var inner = new ProbeableStore { HasContent = false };
        var decorated = new HybridSearchDocumentStore(inner, new HybridRetrievalOptions());

        Assert.False(await ((IDocumentStoreCollectionProbe)decorated).HasContentAsync("c", TestContext.Current.CancellationToken));
        Assert.Equal(1, inner.ProbeCalls);

        inner.HasContent = true;
        Assert.True(await ((IDocumentStoreCollectionProbe)decorated).HasContentAsync("c", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_hybrid_decorator_reports_content_when_the_inner_store_cannot_be_probed()
    {
        var decorated = new HybridSearchDocumentStore(new OpaqueStore(), new HybridRetrievalOptions());

        // "Cannot tell" must mean "assume present": the alternative re-embeds a
        // durable collection on every single run.
        Assert.True(await ((IDocumentStoreCollectionProbe)decorated).HasContentAsync("c", TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// Minimal ingestion harness: one text source on a fake VFS, a stub chunker, and a
/// real <see cref="FileIngestionManifestStore"/> so the manifest genuinely persists
/// between passes — that persistence is the whole point of these tests.
/// </summary>
internal sealed class StaleManifestHarness
{
    private readonly FakeFileSystemService _fs;
    private readonly ChunkingStrategyFactory _chunkingFactory = new();
    private readonly StubChunkingStrategy _chunker = new();
    private readonly FakeEmbeddingProvider _embeddings = new();
    private readonly RagIngestionOptions _options =
        new() { DefaultChunkingStrategy = "stub" };
    private readonly FileIngestionManifestStore _manifestStore;

    public StaleManifestHarness()
    {
        _fs = new FakeFileSystemService().AddMount("/kb").AddMount("/output");
        _fs.AddFile("/kb/a.md", "# A\n\nalpha beta gamma delta epsilon.");
        _chunkingFactory.Register("stub", () => _chunker);
        _manifestStore = new FileIngestionManifestStore(_fs, _options);
    }

    public Task<IngestionReport> IngestAsync(
        IDocumentStore store, bool reindex = false)
    {
        var pipeline = new DefaultIngestionPipeline(
            new DocumentLoaderFactory([new TextFileLoader(_fs)]),
            _chunkingFactory,
            _embeddings,
            store,
            new DataValidationPipeline(
                [new PromptInjectionDocumentValidator(), new ContentIntegrityValidator()],
                new InMemoryQuarantineStore(),
                new ProvenanceTracker()),
            _manifestStore,
            _options);

        return pipeline.IngestAsync(new IngestionRequest
        {
            Collection = "stale",
            Sources = [new SourceDescriptor { Location = "/kb/a.md" }],
            Reindex = reindex,
        });
    }
}
