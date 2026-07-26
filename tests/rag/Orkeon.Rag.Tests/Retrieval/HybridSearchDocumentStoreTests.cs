using System.Collections.Immutable;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Retrieval;
using Orkeon.Rag.Tests.Retrieval.Doubles;
using static Orkeon.Rag.Tests.Retrieval.RetrievalTestData;

namespace Orkeon.Rag.Tests.Retrieval;

/// <summary>
/// Tests of the hybrid <c>IDocumentStore</c> decorator (RAG-04/C2): upserts are delegated
/// AND indexed, searches fuse vector + BM25 with RRF, deletions purge the index, and the
/// native provider hybrid is preferred whenever <c>TryGetCapability</c> finds it.
/// </summary>
public class HybridSearchDocumentStoreTests
{
    private const string Collection = "docs";

    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static HybridRetrievalOptions DefaultOptions() => new() { Enabled = true };

    private static RetrievalQuery Query(string text, int topK = 10) => new()
    {
        Text = text,
        Embedding = ImmutableArray.Create(1f, 0f),
        TopK = topK,
    };

    // --- Emulated path: upsert → index, search → fusion ---

    [Fact]
    public async Task Upsert_DelegatesToInner_AndFeedsTheBm25Index()
    {
        var inner = new SpyDocumentStore();
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());
        var chunk = MakeChunk("c1", "quantum entanglement basics");

        await store.UpsertAsync(Collection, [MakeEmbedded(chunk)], TestCt);

        var upsert = Assert.Single(inner.Upserts);
        Assert.Equal(Collection, upsert.Collection);
        Assert.Same(chunk, Assert.Single(upsert.Chunks).Chunk);

        // Inner vector search returns nothing: only the BM25 side can surface the chunk.
        var results = await store.SearchAsync(Collection, Query("entanglement"), TestCt);
        var single = Assert.Single(results);
        Assert.Equal("c1", single.Chunk.Id);
        Assert.Equal(ReciprocalRankFusion.RrfScoreOrigin, single.ScoreOrigin);
    }

    [Fact]
    public async Task Upsert_InnerRejection_NeverIndexes()
    {
        var inner = new SpyDocumentStore { UpsertException = new ArgumentException("no embedding") };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(Collection, [MakeEmbedded(MakeChunk("c1", "rejected content"))], TestCt));

        inner.UpsertException = null;
        var results = await store.SearchAsync(Collection, Query("rejected"), TestCt);
        Assert.Empty(results); // nothing indexed, nothing from the inner vector list
    }

    [Fact]
    public async Task Search_FusesVectorAndBm25_WithHandComputedRrfOrder()
    {
        // Index: A "red apple pie", C "apple apple tart", B "unrelated zebra".
        // Query "apple" → BM25 ranks [C, A] (tf 2 > tf 1). Vector list scripted [A, B].
        // RRF k=60: A = 1/61 + 1/62 ; C = 1/61 ; B = 1/62 → order A, C, B.
        var chunkA = MakeChunk("A", "red apple pie");
        var chunkB = MakeChunk("B", "unrelated zebra");
        var chunkC = MakeChunk("C", "apple apple tart");

        var inner = new SpyDocumentStore
        {
            SearchResults = [MakeScored(chunkA, 0.95), MakeScored(chunkB, 0.60)],
        };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA), MakeEmbedded(chunkB), MakeEmbedded(chunkC)], TestCt);

        var results = await store.SearchAsync(Collection, Query("apple"), TestCt);

        Assert.Equal(["A", "C", "B"], results.Select(r => r.Chunk.Id));
        Assert.Equal((1.0 / 61) + (1.0 / 62), results[0].Score, precision: 10);
        Assert.Equal(1.0 / 61, results[1].Score, precision: 10);
        Assert.Equal(1.0 / 62, results[2].Score, precision: 10);
        Assert.All(results, r => Assert.Equal(ReciprocalRankFusion.RrfScoreOrigin, r.ScoreOrigin));
    }

    [Fact]
    public async Task Search_TruncatesTheFusionToTopK()
    {
        var chunkA = MakeChunk("A", "apple pie");
        var chunkC = MakeChunk("C", "apple tart");
        var inner = new SpyDocumentStore { SearchResults = [MakeScored(chunkA, 0.9)] };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA), MakeEmbedded(chunkC)], TestCt);

        var results = await store.SearchAsync(Collection, Query("apple", topK: 1), TestCt);

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_NoBm25Contribution_ReturnsInnerHitsUnchanged()
    {
        var chunkA = MakeChunk("A", "alpha content");
        var vectorHits = new[] { MakeScored(chunkA, 0.87, "vector") };
        var inner = new SpyDocumentStore { SearchResults = vectorHits };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA)], TestCt);

        // No lexical match → the vector scores (and their origin) pass through untouched.
        var results = await store.SearchAsync(Collection, Query("zebra"), TestCt);

        var single = Assert.Single(results);
        Assert.Equal("vector", single.ScoreOrigin);
        Assert.Equal(0.87, single.Score, precision: 10);
    }

    [Fact]
    public async Task Search_EmptyQueryText_IsAPurePassthrough()
    {
        var chunkA = MakeChunk("A", "alpha content");
        var inner = new SpyDocumentStore { SearchResults = [MakeScored(chunkA, 0.5)] };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA)], TestCt);

        var results = await store.SearchAsync(
            Collection,
            new RetrievalQuery { Text = "  ", Embedding = ImmutableArray.Create(1f, 0f), TopK = 5 },
            TestCt);

        var single = Assert.Single(results);
        Assert.Equal("vector", single.ScoreOrigin);
        Assert.Single(inner.Searches);
    }

    [Fact]
    public async Task DeleteBySource_DelegatesToInner_AndPurgesTheIndex()
    {
        var inner = new SpyDocumentStore();
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());
        await store.UpsertAsync(Collection,
        [
            MakeEmbedded(MakeChunk("p1", "quantum entanglement", sourceId: "physics.md")),
            MakeEmbedded(MakeChunk("m1", "quantum of solace", sourceId: "movies.md")),
        ], TestCt);

        await store.DeleteBySourceAsync(Collection, "physics.md", TestCt);

        Assert.Equal((Collection, "physics.md"), Assert.Single(inner.Deletes));

        Assert.Empty(await store.SearchAsync(Collection, Query("entanglement"), TestCt));
        var single = Assert.Single(await store.SearchAsync(Collection, Query("solace"), TestCt));
        Assert.Equal("m1", single.Chunk.Id);
    }

    [Fact]
    public async Task Indexes_AreIsolatedPerCollection()
    {
        var inner = new SpyDocumentStore();
        var store = new HybridSearchDocumentStore(inner, DefaultOptions());
        await store.UpsertAsync("alpha", [MakeEmbedded(MakeChunk("a1", "shared keyword"))], TestCt);
        await store.UpsertAsync("beta", [MakeEmbedded(MakeChunk("b1", "shared keyword"))], TestCt);

        var alphaHits = await store.SearchAsync("alpha", Query("keyword"), TestCt);

        Assert.Equal("a1", Assert.Single(alphaHits).Chunk.Id);
    }

    // --- Native path: provider hybrid preferred ---

    [Fact]
    public async Task Search_NativeCapability_IsPreferred_InterfaceLevel_WithCollectionFilter()
    {
        var provider = new FakeHybridSearchMemoryProvider
        {
            HybridResults = [new ScoredMemoryItem(ChunkMemoryItem("n1", "native hit"), 0.91f, "key-1")],
        };
        var inner = new SpyDocumentStore();
        var store = new HybridSearchDocumentStore(inner, DefaultOptions(), provider);

        var query = Query("native question") with
        {
            Filters = ImmutableDictionary<string, string>.Empty.Add("lang", "fr"),
        };
        var results = await store.SearchAsync(Collection, query, TestCt);

        // Native path: the inner store's vector search must NOT run.
        Assert.Empty(inner.Searches);
        Assert.Equal(1, provider.InterfaceLevelCalls);
        Assert.Equal("native question", provider.LastQuery);
        Assert.Equal(10, provider.LastTopK);

        // Default-container layout → the filter carries kind + collection + meta.* criteria.
        var filterProperties = provider.LastFilter!.CustomProperties!;
        Assert.Equal("chunk", filterProperties["rag.kind"]);
        Assert.Equal(Collection, filterProperties["rag.collection"]);
        Assert.Equal("fr", filterProperties["meta.lang"]);

        var single = Assert.Single(results);
        Assert.Equal(HybridSearchDocumentStore.HybridNativeScoreOrigin, single.ScoreOrigin);
        Assert.Equal(0.91, single.Score, precision: 5);
        Assert.Equal("n1", single.Chunk.Id);
        Assert.Equal("native hit", single.Chunk.Content);
        Assert.Equal("fr", single.Chunk.Metadata["lang"]);
    }

    [Fact]
    public async Task Search_NativeCapability_CollectionAwareProvider_UsesTheScopedOverload()
    {
        var provider = new FakeCollectionHybridMemoryProvider
        {
            HybridResults = [new ScoredMemoryItem(ChunkMemoryItem("n1", "native hit"), 0.88f, "key-1")],
        };
        var inner = new SpyDocumentStore();
        var store = new HybridSearchDocumentStore(inner, DefaultOptions(), provider);

        var results = await store.SearchAsync(Collection, Query("native question"), TestCt);

        Assert.Empty(inner.Searches);
        Assert.Equal(0, provider.InterfaceLevelCalls);
        Assert.Equal(Collection, provider.LastHybridCollection);

        // Collection passed natively → no rag.collection criterion in the filter.
        var filterProperties = provider.LastHybridFilter!.CustomProperties!;
        Assert.Equal("chunk", filterProperties["rag.kind"]);
        Assert.False(filterProperties.ContainsKey("rag.collection"));

        Assert.Equal(HybridSearchDocumentStore.HybridNativeScoreOrigin, Assert.Single(results).ScoreOrigin);
    }

    [Fact]
    public async Task Search_NativeScopedOverloadMissing_FallsBackToEmulated()
    {
        // Provider advertises IHybridSearchCapable + ICollectionAwareMemory but never
        // overrode the scoped overload: its interface default throws NotSupported.
        var provider = new FakeCollectionAwareHybridWithoutScopedProvider();
        var chunkA = MakeChunk("A", "fallback keyword");
        var inner = new SpyDocumentStore { SearchResults = [MakeScored(chunkA, 0.7)] };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions(), provider);
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA)], TestCt);

        var results = await store.SearchAsync(Collection, Query("keyword"), TestCt);

        Assert.Equal(0, provider.InterfaceLevelCalls); // scoped was attempted, not the default-container one
        Assert.Single(inner.Searches);                 // emulated path ran
        Assert.Equal(ReciprocalRankFusion.RrfScoreOrigin, Assert.Single(results).ScoreOrigin);
    }

    [Fact]
    public async Task Search_ProviderWithoutHybridCapability_UsesTheEmulatedPath()
    {
        var provider = new Stores.Doubles.FakeMemoryProvider(); // no capability
        var chunkA = MakeChunk("A", "plain keyword");
        var inner = new SpyDocumentStore { SearchResults = [MakeScored(chunkA, 0.7)] };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions(), provider);
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA)], TestCt);

        var results = await store.SearchAsync(Collection, Query("keyword"), TestCt);

        Assert.Single(inner.Searches);
        Assert.Equal(ReciprocalRankFusion.RrfScoreOrigin, Assert.Single(results).ScoreOrigin);
    }

    // --- Per-query hybrid toggle (RAG-04/C4: profiles share one decorator) ---

    [Fact]
    public async Task Search_QueryHybridFalse_IsAStrictPassthrough_EvenWhenEnabled()
    {
        var chunkA = MakeChunk("A", "apple pie");
        var chunkC = MakeChunk("C", "apple tart");
        var inner = new SpyDocumentStore { SearchResults = [MakeScored(chunkA, 0.9, "vector")] };
        var store = new HybridSearchDocumentStore(inner, DefaultOptions()); // Enabled = true
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA), MakeEmbedded(chunkC)], TestCt);

        var results = await store.SearchAsync(
            Collection, Query("apple") with { Hybrid = false }, TestCt);

        // No fusion: the inner hits pass through with their original scores.
        var single = Assert.Single(results);
        Assert.Equal("A", single.Chunk.Id);
        Assert.Equal("vector", single.ScoreOrigin);
        Assert.Equal(0.9, single.Score, precision: 10);
    }

    [Fact]
    public async Task Search_QueryHybridTrue_Fuses_EvenWhenTheDefaultModeIsDisabled()
    {
        var chunkA = MakeChunk("A", "apple pie");
        var chunkC = MakeChunk("C", "apple apple tart");
        var inner = new SpyDocumentStore { SearchResults = [MakeScored(chunkA, 0.9)] };
        var store = new HybridSearchDocumentStore(inner, new HybridRetrievalOptions()); // Enabled = false
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA), MakeEmbedded(chunkC)], TestCt);

        var results = await store.SearchAsync(
            Collection, Query("apple") with { Hybrid = true }, TestCt);

        Assert.Equal(2, results.Count); // C surfaced by BM25 despite the disabled default
        Assert.All(results, r => Assert.Equal(ReciprocalRankFusion.RrfScoreOrigin, r.ScoreOrigin));
    }

    [Fact]
    public async Task Search_NoQueryToggle_FollowsTheConfiguredDefault()
    {
        var chunkA = MakeChunk("A", "apple pie");
        var chunkC = MakeChunk("C", "apple apple tart");
        var inner = new SpyDocumentStore { SearchResults = [MakeScored(chunkA, 0.9, "vector")] };
        var store = new HybridSearchDocumentStore(inner, new HybridRetrievalOptions()); // Enabled = false
        await store.UpsertAsync(Collection, [MakeEmbedded(chunkA), MakeEmbedded(chunkC)], TestCt);

        var results = await store.SearchAsync(Collection, Query("apple"), TestCt);

        // Default mode disabled → passthrough (upserts still fed the index for later).
        var single = Assert.Single(results);
        Assert.Equal("vector", single.ScoreOrigin);
    }

    // --- Guards ---

    [Fact]
    public void Constructor_RejectsNullsAndInvalidRrfK()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HybridSearchDocumentStore(null!, DefaultOptions()));
        Assert.Throws<ArgumentNullException>(() =>
            new HybridSearchDocumentStore(new SpyDocumentStore(), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridSearchDocumentStore(new SpyDocumentStore(), new HybridRetrievalOptions { RrfK = 0 }));
    }

    [Fact]
    public async Task Search_NullQuery_Throws()
    {
        var store = new HybridSearchDocumentStore(new SpyDocumentStore(), DefaultOptions());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.SearchAsync(Collection, null!, TestCt));
    }
}
