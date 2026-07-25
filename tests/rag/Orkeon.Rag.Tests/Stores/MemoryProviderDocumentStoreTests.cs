using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Stores;
using Orkeon.Rag.Tests.Stores.Doubles;

namespace Orkeon.Rag.Tests.Stores;

/// <summary>
/// Tests <see cref="MemoryProviderDocumentStore"/> against the three provider shapes:
/// capability provider (native scores), legacy vector provider, and bare provider
/// (local-cosine fallback). Covers round-trip fidelity, key schema, collection isolation,
/// TopK, query filters, and delete-by-source.
/// </summary>
public partial class MemoryProviderDocumentStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("^rag:docs:[0-9a-f]{16}:3$")]
    private static partial Regex ChunkKeyIndex3Regex();

    [GeneratedRegex("^rag:docs:[0-9a-f]{16}:[0-9]+$")]
    private static partial Regex ChunkKeyRegex();

    [GeneratedRegex("^rag:docs:[0-9a-f]{16}:manifest$")]
    private static partial Regex ManifestKeyRegex();

    private static EmbeddedChunk MakeChunk(
        string id,
        float[] embedding,
        string sourceId = "src-1",
        string documentId = "doc-1",
        string content = "some content",
        int index = 0,
        int startOffset = 0,
        int endOffset = 12,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new EmbeddedChunk
        {
            Chunk = new Chunk
            {
                Id = id,
                DocumentId = documentId,
                SourceId = sourceId,
                Content = content,
                Index = index,
                StartOffset = startOffset,
                EndOffset = endOffset,
                Metadata = metadata is null
                    ? ImmutableDictionary<string, string>.Empty
                    : metadata.ToImmutableDictionary(),
            },
            Embedding = [.. embedding],
        };
    }

    private static RetrievalQuery MakeQuery(
        float[] embedding,
        int topK = 50,
        IReadOnlyDictionary<string, string>? filters = null)
    {
        return new RetrievalQuery
        {
            Text = "query",
            Embedding = [.. embedding],
            TopK = topK,
            Filters = filters is null
                ? ImmutableDictionary<string, string>.Empty
                : filters.ToImmutableDictionary(),
        };
    }

    // ---------------------------------------------------------------- construction & guards

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MemoryProviderDocumentStore(null!));
    }

    [Fact]
    public async Task SearchAsync_WithoutEmbedding_ThrowsArgumentException()
    {
        var store = new MemoryProviderDocumentStore(new FakeScoredVectorMemoryProvider());
        var query = new RetrievalQuery { Text = "no embedding" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => store.SearchAsync("docs", query, Ct));

        Assert.Contains("Embedding", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_NonPositiveTopK_ThrowsArgumentException()
    {
        var store = new MemoryProviderDocumentStore(new FakeScoredVectorMemoryProvider());
        var query = MakeQuery([1f, 0f, 0f], topK: 0);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync("docs", query, Ct));
    }

    [Fact]
    public async Task UpsertAsync_CollectionWithColon_ThrowsArgumentException()
    {
        var store = new MemoryProviderDocumentStore(new FakeMemoryProvider());

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.UpsertAsync("bad:name", [MakeChunk("c1", [1f, 0f, 0f])], Ct));
    }

    [Fact]
    public async Task UpsertAsync_ChunkWithoutEmbedding_ThrowsArgumentException()
    {
        var store = new MemoryProviderDocumentStore(new FakeMemoryProvider());
        var chunk = new EmbeddedChunk
        {
            Chunk = MakeChunk("c1", [1f]).Chunk,
            Embedding = ImmutableArray<float>.Empty,
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => store.UpsertAsync("docs", [chunk], Ct));

        Assert.Contains("embedding", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- upsert & key schema

    [Fact]
    public async Task UpsertAsync_KeySchema_IsStableAndPrefixed()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);

        await store.UpsertAsync(
            "docs", [MakeChunk("c1", [1f, 0f, 0f], sourceId: "guide.md", index: 3)], Ct);

        var chunkKey = Assert.Single(provider.Items.Keys, key => ChunkKeyIndex3Regex().IsMatch(key));
        Assert.Single(provider.Items.Keys, key => ManifestKeyRegex().IsMatch(key));
        Assert.Contains("rag:docs:sources", provider.Items.Keys);

        // Same chunk again: same deterministic key, no duplicate entries.
        var countBefore = provider.Items.Count;
        await store.UpsertAsync(
            "docs", [MakeChunk("c1", [1f, 0f, 0f], sourceId: "guide.md", index: 3)], Ct);
        Assert.Equal(countBefore, provider.Items.Count);
        Assert.Contains(chunkKey, provider.Items.Keys);
    }

    [Fact]
    public async Task UpsertAsync_WithBatchCapability_UsesSingleBatch()
    {
        var provider = new FakeScoredVectorMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);

        await store.UpsertAsync("docs",
        [
            MakeChunk("c1", [1f, 0f, 0f], index: 0),
            MakeChunk("c2", [0f, 1f, 0f], index: 1),
            MakeChunk("c3", [0f, 0f, 1f], index: 2),
        ], Ct);

        Assert.Equal(1, provider.BatchUpsertCalls);
        Assert.Equal(3, provider.Items.Values.Count(item => item.Embedding is { Count: 3 }));
    }

    [Fact]
    public async Task UpsertAsync_WithoutBatchCapability_StoresEachChunkWithEmbedding()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);

        await store.UpsertAsync("docs",
        [
            MakeChunk("c1", [1f, 0f, 0f], index: 0),
            MakeChunk("c2", [0f, 1f, 0f], index: 1),
        ], Ct);

        var chunkItems = provider.Items
            .Where(pair => ChunkKeyRegex().IsMatch(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        Assert.Equal(2, chunkItems.Count);
        Assert.All(chunkItems, item => Assert.Equal(3, item.Embedding?.Count));
    }

    [Fact]
    public async Task UpsertAsync_EmptyBatch_IsNoOp()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);

        await store.UpsertAsync("docs", [], Ct);

        Assert.Empty(provider.Items);
    }

    // ---------------------------------------------------------------- search: score origins

    [Fact]
    public async Task SearchAsync_WithScoredCapability_ReturnsNativeScores_VectorOrigin()
    {
        var provider = new FakeScoredVectorMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("best", [1f, 0f, 0f], index: 0),
            MakeChunk("mid", [0.9f, 0.1f, 0f], index: 1),
            MakeChunk("far", [0f, 1f, 0f], index: 2),
        ], Ct);

        var results = await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f]), Ct);

        Assert.Equal(1, provider.ScoredSearchCalls);
        Assert.Equal(3, results.Count);
        Assert.Equal(["best", "mid", "far"], results.Select(r => r.Chunk.Id).ToArray());
        Assert.All(results, r => Assert.Equal("vector", r.ScoreOrigin));
        Assert.Equal(1.0, results[0].Score, 6);
        Assert.Equal(0.0, results[2].Score, 6);
        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    [Fact]
    public async Task SearchAsync_LegacyVectorProvider_UsesLegacyScores_VectorOrigin()
    {
        var provider = new FakeLegacyVectorMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("best", [1f, 0f, 0f], index: 0),
            MakeChunk("far", [0f, 1f, 0f], index: 1),
        ], Ct);

        var results = await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f]), Ct);

        Assert.Equal(1, provider.LegacySearchCalls);
        Assert.Equal(2, results.Count);
        Assert.Equal("best", results[0].Chunk.Id);
        Assert.All(results, r => Assert.Equal("vector", r.ScoreOrigin));
        Assert.Equal(1.0, results[0].Score, 6);
    }

    [Fact]
    public async Task SearchAsync_BareProvider_FallsBackToLocalCosine()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("best", [1f, 0f, 0f], index: 0),
            MakeChunk("mid", [0.5f, 0.5f, 0f], index: 1),
            MakeChunk("far", [0f, 0f, 1f], index: 2),
        ], Ct);

        var results = await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f]), Ct);

        Assert.Equal(3, results.Count);
        Assert.Equal(["best", "mid", "far"], results.Select(r => r.Chunk.Id).ToArray());
        Assert.All(results, r => Assert.Equal("local-cosine", r.ScoreOrigin));
        Assert.Equal(1.0, results[0].Score, 6);
        Assert.Equal(Math.Sqrt(0.5), results[1].Score, 6);
        Assert.Equal(0.0, results[2].Score, 6);
    }

    // ---------------------------------------------------------------- round-trip fidelity

    [Fact]
    public async Task SearchAsync_RoundTrip_ReconstructsChunkFieldByField()
    {
        var provider = new FakeScoredVectorMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        var original = MakeChunk(
            "chunk-42",
            [1f, 0f, 0f],
            sourceId: "s3://bucket/report.pdf",
            documentId: "doc-7",
            content: "Cosine similarity measures the angle between vectors.",
            index: 42,
            startOffset: 1200,
            endOffset: 1253,
            metadata: new Dictionary<string, string> { ["lang"] = "fr", ["topic"] = "math" });

        await store.UpsertAsync("kb", [original], Ct);
        var results = await store.SearchAsync("kb", MakeQuery([1f, 0f, 0f]), Ct);

        var chunk = Assert.Single(results).Chunk;
        Assert.Equal(original.Chunk.Id, chunk.Id);
        Assert.Equal(original.Chunk.DocumentId, chunk.DocumentId);
        Assert.Equal(original.Chunk.SourceId, chunk.SourceId);
        Assert.Equal(original.Chunk.Content, chunk.Content);
        Assert.Equal(original.Chunk.Index, chunk.Index);
        Assert.Equal(original.Chunk.StartOffset, chunk.StartOffset);
        Assert.Equal(original.Chunk.EndOffset, chunk.EndOffset);
        Assert.Equal(2, chunk.Metadata.Count);
        Assert.Equal("fr", chunk.Metadata["lang"]);
        Assert.Equal("math", chunk.Metadata["topic"]);
    }

    [Fact]
    public async Task SearchAsync_RoundTripThroughLocalCosine_AlsoReconstructsMetadata()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        var original = MakeChunk(
            "chunk-1",
            [0f, 1f, 0f],
            index: 5,
            startOffset: 10,
            endOffset: 22,
            metadata: new Dictionary<string, string> { ["section"] = "intro" });

        await store.UpsertAsync("kb", [original], Ct);
        var results = await store.SearchAsync("kb", MakeQuery([0f, 1f, 0f]), Ct);

        var chunk = Assert.Single(results).Chunk;
        Assert.Equal(5, chunk.Index);
        Assert.Equal(10, chunk.StartOffset);
        Assert.Equal(22, chunk.EndOffset);
        Assert.Equal("intro", chunk.Metadata["section"]);
    }

    // ---------------------------------------------------------------- TopK, filters, isolation

    [Fact]
    public async Task SearchAsync_RespectsTopK_OnCapabilityAndLocalPaths()
    {
        foreach (FakeMemoryProvider provider in new FakeMemoryProvider[]
                 { new FakeScoredVectorMemoryProvider(), new FakeMemoryProvider() })
        {
            var store = new MemoryProviderDocumentStore(provider);
            await store.UpsertAsync("docs",
            [
                MakeChunk("c0", [1f, 0f, 0f], index: 0),
                MakeChunk("c1", [0.9f, 0.1f, 0f], index: 1),
                MakeChunk("c2", [0.5f, 0.5f, 0f], index: 2),
                MakeChunk("c3", [0f, 1f, 0f], index: 3),
            ], Ct);

            var results = await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f], topK: 2), Ct);

            Assert.Equal(2, results.Count);
            Assert.Equal(["c0", "c1"], results.Select(r => r.Chunk.Id).ToArray());
        }
    }

    [Fact]
    public async Task SearchAsync_QueryFilters_NarrowResults()
    {
        var provider = new FakeScoredVectorMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("fr", [1f, 0f, 0f], index: 0,
                metadata: new Dictionary<string, string> { ["lang"] = "fr" }),
            MakeChunk("en", [1f, 0f, 0f], index: 1,
                metadata: new Dictionary<string, string> { ["lang"] = "en" }),
        ], Ct);

        var results = await store.SearchAsync(
            "docs",
            MakeQuery([1f, 0f, 0f], filters: new Dictionary<string, string> { ["lang"] = "fr" }),
            Ct);

        var hit = Assert.Single(results);
        Assert.Equal("fr", hit.Chunk.Id);

        // The filter reached the provider as metadata custom properties.
        Assert.NotNull(provider.LastFilter);
        Assert.Equal("fr", provider.LastFilter!.CustomProperties!["meta.lang"]);
        Assert.Equal("docs", provider.LastFilter.CustomProperties["rag.collection"]);
    }

    [Fact]
    public async Task SearchAsync_QueryFilters_NarrowResults_OnLocalCosinePath()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("fr", [1f, 0f, 0f], index: 0,
                metadata: new Dictionary<string, string> { ["lang"] = "fr" }),
            MakeChunk("en", [1f, 0f, 0f], index: 1,
                metadata: new Dictionary<string, string> { ["lang"] = "en" }),
        ], Ct);

        var results = await store.SearchAsync(
            "docs",
            MakeQuery([1f, 0f, 0f], filters: new Dictionary<string, string> { ["lang"] = "en" }),
            Ct);

        var hit = Assert.Single(results);
        Assert.Equal("en", hit.Chunk.Id);
    }

    [Fact]
    public async Task SearchAsync_CollectionsAreIsolated()
    {
        foreach (FakeMemoryProvider provider in new FakeMemoryProvider[]
                 { new FakeScoredVectorMemoryProvider(), new FakeMemoryProvider() })
        {
            var store = new MemoryProviderDocumentStore(provider);
            await store.UpsertAsync(
                "alpha", [MakeChunk("a1", [1f, 0f, 0f], content: "alpha chunk")], Ct);
            await store.UpsertAsync(
                "beta", [MakeChunk("b1", [1f, 0f, 0f], content: "beta chunk")], Ct);

            var alphaHits = await store.SearchAsync("alpha", MakeQuery([1f, 0f, 0f]), Ct);
            var betaHits = await store.SearchAsync("beta", MakeQuery([1f, 0f, 0f]), Ct);

            Assert.Equal("a1", Assert.Single(alphaHits).Chunk.Id);
            Assert.Equal("b1", Assert.Single(betaHits).Chunk.Id);
        }
    }

    // ---------------------------------------------------------------- delete by source

    [Fact]
    public async Task DeleteBySourceAsync_RemovesOnlyThatSource()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("k1", [1f, 0f, 0f], sourceId: "keep.md", index: 0),
            MakeChunk("d1", [1f, 0f, 0f], sourceId: "drop.md", index: 0),
            MakeChunk("d2", [0f, 1f, 0f], sourceId: "drop.md", index: 1),
        ], Ct);

        await store.DeleteBySourceAsync("docs", "drop.md", Ct);

        var results = await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f]), Ct);
        Assert.Equal("k1", Assert.Single(results).Chunk.Id);

        // Chunk entries and manifest of the dropped source are gone.
        Assert.DoesNotContain(provider.Items.Values, item => item.Source == "drop.md");
        Assert.Single(provider.Items.Keys, key => ManifestKeyRegex().IsMatch(key));
    }

    [Fact]
    public async Task DeleteBySourceAsync_UnknownSource_IsNoOp()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs", [MakeChunk("c1", [1f, 0f, 0f], sourceId: "keep.md")], Ct);
        var countBefore = provider.Items.Count;

        await store.DeleteBySourceAsync("docs", "never-ingested.md", Ct);

        Assert.Equal(countBefore, provider.Items.Count);
    }

    [Fact]
    public async Task DeleteBySourceAsync_LastSource_RemovesRegistry()
    {
        var provider = new FakeMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs", [MakeChunk("c1", [1f, 0f, 0f], sourceId: "only.md")], Ct);

        await store.DeleteBySourceAsync("docs", "only.md", Ct);

        Assert.Empty(provider.Items);
        Assert.Empty(await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f]), Ct));
    }

    [Fact]
    public async Task DeleteBySourceAsync_SameSourceInOtherCollection_IsUntouched()
    {
        var provider = new FakeScoredVectorMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("alpha", [MakeChunk("a1", [1f, 0f, 0f], sourceId: "shared.md")], Ct);
        await store.UpsertAsync("beta", [MakeChunk("b1", [1f, 0f, 0f], sourceId: "shared.md")], Ct);

        await store.DeleteBySourceAsync("alpha", "shared.md", Ct);

        Assert.Empty(await store.SearchAsync("alpha", MakeQuery([1f, 0f, 0f]), Ct));
        var betaHits = await store.SearchAsync("beta", MakeQuery([1f, 0f, 0f]), Ct);
        Assert.Equal("b1", Assert.Single(betaHits).Chunk.Id);
    }
}
