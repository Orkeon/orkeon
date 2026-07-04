using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Core.Cache;
using Orkeon.Analysis.Core.Serialization;
using Orkeon.Analysis.Vectors;
using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Memory;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// LE-12 — Regression tests pinning the dimension flow through the three downstream
/// components that historically hard-coded 1536 (OpenAI's text-embedding-3-small).
/// Each test feeds a 384-dim mock provider and asserts the dimension travels end-to-end
/// without any constant overriding it.
/// </summary>
/// <remarks>
/// Fast suite — pure mocks, no ONNX boot, no I/O outside <c>TestFixtures.WriteDirectoryAsync</c>
/// for the cache test (which needs a manifest on disk).
/// </remarks>
public class EmbeddingDimensionPropagationTests
{
    private const int LocalDimensions = 384;
    private const int OpenAiDimensions = 1536;

    // ----- 1. InMemoryRaggableStore — accepts and scores 384-dim vectors -----

    [Fact]
    public async Task InMemoryRaggableStore_stores_and_searches_with_384_dim_vectors()
    {
        // Arrange — three nodes with deterministic 384-dim embeddings; query embedder
        // returns the first node's vector so cosine similarity peaks at id-0.
        var nodes = new[]
        {
            BuildNode("id-0", FillVector(LocalDimensions, seed: 0.10f)),
            BuildNode("id-1", FillVector(LocalDimensions, seed: 0.20f)),
            BuildNode("id-2", FillVector(LocalDimensions, seed: 0.30f)),
        };
        var queryVec = nodes[0].Embedding!.Value;

        var store = new InMemoryRaggableStore(
            (IReadOnlyList<RaggableNode>)nodes,
            [],
            new FakeFileSystemService(),
            queryEmbedder: (_, __) => Task.FromResult<ReadOnlyMemory<float>?>(queryVec));

        // Act
        var hits = await store.SemanticSearchAsync(
            new SemanticQuery { Text = "anything", TopK = 3 }, CancellationToken.None);

        // Assert — 3 hits, ranked by descending score, all scores in [0, 1].
        Assert.Equal(3, hits.Count);
        Assert.Equal("pkg::id-0", hits[0].Fqn);
        Assert.True(hits[0].Score >= hits[1].Score);
        Assert.True(hits[1].Score >= hits[2].Score);
        Assert.All(hits, h =>
        {
            Assert.True(h.Score >= 0 && h.Score <= 1.0001,
                $"cosine out of [0,1]: {h.Score}");
        });
    }

    // ----- 2. MemoryProviderVectorStoreAdapter — propagates the active dimension -----

    [Fact]
    public async Task MemoryProviderVectorStoreAdapter_passes_through_provider_dimensions()
    {
        // Arrange — capturing memory provider records the embedding length seen on the wire.
        var provider = new DimensionCapturingMemoryProvider();
        var adapter = new MemoryProviderVectorStoreAdapter(provider);

        var localVec = FillVector(LocalDimensions, seed: 0.5f);
        var doc = new VectorDocument(
            "id-1", "text", localVec,
            new VectorMetadata
            {
                Kind = "Method",
                Language = "typescript",
                VirtualFilePath = "/tmp/x.ts",
                Fqn = "pkg.x",
            });

        // Act — index then search using 384-dim vectors only.
        await adapter.IndexAsync([doc], CancellationToken.None);
        await adapter.SearchAsync(
            "q", localVec, topK: 5, filters: null, CancellationToken.None);

        // Assert — the dimension reaching IMemoryProvider matches the active provider's.
        Assert.NotNull(provider.LastIndexedEmbeddingLength);
        Assert.Equal(LocalDimensions, provider.LastIndexedEmbeddingLength);

        Assert.NotNull(provider.LastQueryEmbeddingLength);
        Assert.Equal(LocalDimensions, provider.LastQueryEmbeddingLength);

        // Sanity — switching to 1536 dims propagates the new size, no constant lurking.
        var openAiVec = FillVector(OpenAiDimensions, seed: 0.5f);
        var doc2 = doc with { Embedding = openAiVec };
        await adapter.IndexAsync([doc2], CancellationToken.None);
        Assert.Equal(OpenAiDimensions, provider.LastIndexedEmbeddingLength);
    }

    // ----- 3. RaggableTreeCache — clean miss when embedding dimension switches -----

    [Fact]
    public async Task RaggableTreeCache_invalidates_cleanly_when_switching_from_1536_to_384()
    {
        // Arrange — physical dir + VFS mount.
        var dir = await TestFixtures.WriteDirectoryAsync(
        [
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);

            var openAiProfile = new EmbeddingCacheProfile
            {
                Provider = "OpenAI",
                Model = "text-embedding-3-small",
                Dimensions = OpenAiDimensions,
            };
            var localProfile = new EmbeddingCacheProfile
            {
                Provider = "LocalSmartComponents",
                Model = "bge-micro-v2",
                Dimensions = LocalDimensions,
            };

            // 1) Save with 1536-dim profile.
            var saveCache = new RaggableTreeCache(
                new RaggableTreeSerializer(),
                new FileSystemDiscoverer(fs),
                fs,
                adapterVersions: new Dictionary<string, string> { ["typescript"] = "1.0" },
                embeddingProfile: openAiProfile);

            var tree = BuildTinyTree();
            await saveCache.SaveAsync(tree, "idx-1", TestFixtures.TestVirtualRoot, CancellationToken.None);
            Assert.True(
                await saveCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None),
                "cache must be valid against the same profile right after save");

            // 2) Reload with the 384-dim profile — must observe a clean miss without throwing.
            var loadCache = new RaggableTreeCache(
                new RaggableTreeSerializer(),
                new FileSystemDiscoverer(fs),
                fs,
                adapterVersions: new Dictionary<string, string> { ["typescript"] = "1.0" },
                embeddingProfile: localProfile);

            // Act — the LE-10 contract: this must not throw, must report a miss.
            var loaded = await loadCache.LoadAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);
            var valid = await loadCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);

            // Assert
            Assert.False(loaded.Hit, "cross-dimension cache must report a miss");
            Assert.Null(loaded.Tree);
            Assert.False(valid, "cross-dimension cache must be reported invalid");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- Helpers -----

    private static RaggableNode BuildNode(string id, ReadOnlyMemory<float> embedding) => new()
    {
        Id = id,
        Kind = UniversalNodeKind.Function,
        Name = id,
        VirtualFilePath = $"/tmp/{id}.ts",
        Range = new NodeRange(0, 0, 1, 2, 0),
        Level = NodeLevel.L3_Symbol,
        Language = "typescript",
        SourceSnippet = "fn",
        Sha256 = string.Empty,
        Fqn = $"pkg::{id}",
        Embedding = embedding,
    };

    private static ReadOnlyMemory<float> FillVector(int length, float seed)
    {
        var v = new float[length];
        for (var i = 0; i < length; i++)
        {
            // Drift each element so different seeds yield distinguishable vectors
            // while staying numerically tame.
            v[i] = seed + (i % 17) * 0.001f;
        }
        return v;
    }

    private static RaggableTree BuildTinyTree()
    {
        var node = new RaggableNode
        {
            Id = "sym::x",
            Kind = UniversalNodeKind.Method,
            Name = "x",
            VirtualFilePath = "/tmp/x.ts",
            Range = new NodeRange(0, 5, 1, 2, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "fn",
            Sha256 = "abc",
            Fqn = "pkg::x",
        };
        return new RaggableTree([node], [], new Dictionary<string, RaggableNode> { [node.Fqn] = node });
    }

    /// <summary>
    /// In-memory <see cref="IMemoryProvider"/> that records the length of every embedding
    /// it sees, so the test can assert the active provider's dimension is the one travelling
    /// through <see cref="MemoryProviderVectorStoreAdapter"/> — not a hard-coded constant.
    /// </summary>
    private sealed class DimensionCapturingMemoryProvider : IMemoryProvider
    {
        private readonly ConcurrentDictionary<string, MemoryItem> _store = new();

        public int? LastIndexedEmbeddingLength { get; private set; }
        public int? LastQueryEmbeddingLength { get; private set; }

        public Task StoreAsync(string key, MemoryItem item, CancellationToken ct = default)
        {
            _store[key] = item;
            return Task.CompletedTask;
        }

        public Task<MemoryItem?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

        public Task<IEnumerable<MemoryItem>> SearchAsync(
            string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<MemoryItem>>(_store.Values.Take(limit));

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_store.TryRemove(key, out _));

        public Task ClearAsync(CancellationToken ct = default)
        {
            _store.Clear();
            return Task.CompletedTask;
        }

        public Task StoreWithEmbeddingAsync(
            string key, MemoryItem item, float[] embedding, CancellationToken ct = default)
        {
            // Capture the dimension actually reaching the memory provider — the whole point
            // of this test. Storing the item itself is enough; we do not need to round-trip
            // the embedding back through MemoryItem.Embedding (its setter is internal to
            // Orkeon.Domain and not callable from tests).
            LastIndexedEmbeddingLength = embedding.Length;
            _store[key] = item;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
            float[] queryEmbedding, int topK = 10, float minScore = 0,
            Dictionary<string, object>? filter = null, CancellationToken ct = default)
        {
            LastQueryEmbeddingLength = queryEmbedding.Length;
            // We don't need actual scoring here — the assertion targets the captured
            // dimension, not the hit list. Return an empty result.
            return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([]);
        }
    }
}
