using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Stores;
using Orkeon.Rag.Tests.Stores.Doubles;

namespace Orkeon.Rag.Tests.Stores;

/// <summary>
/// Tests the native-collections path of <see cref="MemoryProviderDocumentStore"/>
/// (RAG-03/C2): when the provider exposes <c>ICollectionAwareMemory</c>, the collection is
/// passed to the provider, keys carry no <c>rag:</c> prefix, no manifest/registry entries
/// are written, and deletion by source compiles to a native source filter.
/// </summary>
public partial class MemoryProviderDocumentStoreNativeCollectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("^[0-9a-f]{16}:[0-9]+$")]
    private static partial Regex NativeChunkKeyRegex();

    private static EmbeddedChunk MakeChunk(
        string id,
        float[] embedding,
        string sourceId = "src-1",
        string documentId = "doc-1",
        string content = "some content",
        int index = 0,
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
                StartOffset = 0,
                EndOffset = content.Length,
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

    // ---------------------------------------------------------------- upsert

    [Fact]
    public async Task UpsertAsync_NativePath_PassesCollectionAndUnprefixedKeys()
    {
        var provider = new FakeCollectionAwareMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);

        await store.UpsertAsync("docs",
        [
            MakeChunk("c1", [1f, 0f, 0f], sourceId: "guide.md", index: 0),
            MakeChunk("c2", [0f, 1f, 0f], sourceId: "guide.md", index: 1),
        ], Ct);

        // One scoped batch, targeting the RAG collection.
        Assert.Equal(["docs"], provider.UpsertCollections);

        // Keys are collection-scoped: {sourceHash}:{index}, no "rag:" prefix.
        Assert.Equal(2, provider.ReceivedKeys.Count);
        Assert.All(provider.ReceivedKeys, key => Assert.Matches(NativeChunkKeyRegex(), key));
        Assert.All(provider.ReceivedKeys, key =>
            Assert.DoesNotContain("rag:", key, StringComparison.Ordinal));

        // No manifest/registry entries — only the two chunks live in the collection.
        Assert.Equal(2, provider.Collections["docs"].Count);

        // The base (unscoped) provider surface was never touched.
        Assert.Empty(provider.Items);
    }

    [Fact]
    public async Task UpsertAsync_NativePath_SameChunkTwice_KeepsOneEntry()
    {
        var provider = new FakeCollectionAwareMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);

        await store.UpsertAsync("docs", [MakeChunk("c1", [1f, 0f, 0f], index: 3)], Ct);
        await store.UpsertAsync("docs", [MakeChunk("c1", [1f, 0f, 0f], index: 3)], Ct);

        Assert.Single(provider.Collections["docs"]);
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public async Task SearchAsync_NativePath_PassesCollection_AndReturnsVectorOrigin()
    {
        var provider = new FakeCollectionAwareMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("best", [1f, 0f, 0f], index: 0),
            MakeChunk("far", [0f, 1f, 0f], index: 1),
        ], Ct);

        var results = await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f]), Ct);

        Assert.Equal("docs", provider.LastSearchCollection);
        Assert.Equal(2, results.Count);
        Assert.Equal("best", results[0].Chunk.Id);
        Assert.All(results, r => Assert.Equal(MemoryProviderDocumentStore.VectorScoreOrigin, r.ScoreOrigin));
        Assert.Equal(1.0, results[0].Score, 6);
    }

    [Fact]
    public async Task SearchAsync_NativePath_FilterCarriesNoCollectionProperty()
    {
        var provider = new FakeCollectionAwareMemoryProvider();
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

        Assert.Equal("fr", Assert.Single(results).Chunk.Id);

        // Collection scoping is the provider's job — the filter only narrows on kind + metadata.
        Assert.NotNull(provider.LastSearchFilter);
        var properties = provider.LastSearchFilter!.CustomProperties!;
        Assert.Equal("fr", properties["meta.lang"]);
        Assert.Equal("chunk", properties["rag.kind"]);
        Assert.False(properties.ContainsKey("rag.collection"));
    }

    [Fact]
    public async Task SearchAsync_NativePath_RoundTripsChunkMetadata()
    {
        var provider = new FakeCollectionAwareMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        var original = MakeChunk(
            "chunk-42",
            [1f, 0f, 0f],
            sourceId: "s3://bucket/report.pdf",
            documentId: "doc-7",
            content: "Cosine similarity measures the angle between vectors.",
            index: 42,
            metadata: new Dictionary<string, string> { ["lang"] = "fr" });

        await store.UpsertAsync("kb", [original], Ct);
        var results = await store.SearchAsync("kb", MakeQuery([1f, 0f, 0f]), Ct);

        var chunk = Assert.Single(results).Chunk;
        Assert.Equal("chunk-42", chunk.Id);
        Assert.Equal("doc-7", chunk.DocumentId);
        Assert.Equal("s3://bucket/report.pdf", chunk.SourceId);
        Assert.Equal(original.Chunk.Content, chunk.Content);
        Assert.Equal(42, chunk.Index);
        Assert.Equal("fr", chunk.Metadata["lang"]);
    }

    [Fact]
    public async Task SearchAsync_NativePath_CollectionsAreIsolated()
    {
        var provider = new FakeCollectionAwareMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("alpha", [MakeChunk("a1", [1f, 0f, 0f], content: "alpha chunk")], Ct);
        await store.UpsertAsync("beta", [MakeChunk("b1", [1f, 0f, 0f], content: "beta chunk")], Ct);

        var alphaHits = await store.SearchAsync("alpha", MakeQuery([1f, 0f, 0f]), Ct);
        var betaHits = await store.SearchAsync("beta", MakeQuery([1f, 0f, 0f]), Ct);

        Assert.Equal("a1", Assert.Single(alphaHits).Chunk.Id);
        Assert.Equal("b1", Assert.Single(betaHits).Chunk.Id);
    }

    // ---------------------------------------------------------------- delete by source

    [Fact]
    public async Task DeleteBySourceAsync_NativePath_UsesSourceFilterOnTheCollection()
    {
        var provider = new FakeCollectionAwareMemoryProvider();
        var store = new MemoryProviderDocumentStore(provider);
        await store.UpsertAsync("docs",
        [
            MakeChunk("k1", [1f, 0f, 0f], sourceId: "keep.md", index: 0),
            MakeChunk("d1", [1f, 0f, 0f], sourceId: "drop.md", index: 0),
            MakeChunk("d2", [0f, 1f, 0f], sourceId: "drop.md", index: 1),
        ], Ct);

        await store.DeleteBySourceAsync("docs", "drop.md", Ct);

        Assert.Equal("docs", provider.LastDeleteCollection);
        Assert.NotNull(provider.LastDeleteFilter);
        Assert.Equal("drop.md", provider.LastDeleteFilter!.Source);

        var results = await store.SearchAsync("docs", MakeQuery([1f, 0f, 0f]), Ct);
        Assert.Equal("k1", Assert.Single(results).Chunk.Id);
    }
}
