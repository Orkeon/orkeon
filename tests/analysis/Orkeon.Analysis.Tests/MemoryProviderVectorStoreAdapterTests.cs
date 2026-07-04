using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Vectors;
using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Memory;

namespace Orkeon.Analysis.Tests;

public class MemoryProviderVectorStoreAdapterTests
{
    [Fact]
    public async Task IndexAsync_stores_each_document()
    {
        var provider = new FakeMemoryProvider();
        var adapter = new MemoryProviderVectorStoreAdapter(provider);

        var docs = new[]
        {
            BuildDoc("id-1", "fqn-1"),
            BuildDoc("id-2", "fqn-2"),
        };

        await adapter.IndexAsync(docs, CancellationToken.None);

        Assert.Equal(2, provider.Count);
    }

    [Fact]
    public async Task SearchAsync_returns_hits_with_score_metadata_and_id()
    {
        var provider = new FakeMemoryProvider();
        var adapter = new MemoryProviderVectorStoreAdapter(provider);

        var docs = new[]
        {
            BuildDoc("id-a", "fqn-a", [1f, 0f], "Method"),
            BuildDoc("id-b", "fqn-b", [0f, 1f], "Class"),
        };
        await adapter.IndexAsync(docs, CancellationToken.None);

        var hits = await adapter.SearchAsync(
            "query", new ReadOnlyMemory<float>([1f, 0f]), 2, null, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        var top = hits[0];
        Assert.Equal("id-a", top.Id);
        Assert.Equal("fqn-a", top.Metadata.Fqn);
        Assert.Equal("Method", top.Metadata.Kind);
        Assert.True(top.Score > hits[1].Score);
    }

    [Fact]
    public async Task SearchAsync_filters_by_metadata_kind()
    {
        var provider = new FakeMemoryProvider();
        var adapter = new MemoryProviderVectorStoreAdapter(provider);

        await adapter.IndexAsync(
        [
            BuildDoc("id-a", "fqn-a", [1f, 0f], "Method"),
            BuildDoc("id-b", "fqn-b", [0.9f, 0.1f], "Class"),
        ], CancellationToken.None);

        var filters = new Dictionary<string, object> { ["kind"] = "Class" };
        var hits = await adapter.SearchAsync(
            "query", new ReadOnlyMemory<float>([1f, 0f]), 5, filters, CancellationToken.None);

        Assert.Single(hits);
        Assert.Equal("id-b", hits[0].Id);
    }

    [Fact]
    public async Task DeleteAsync_removes_documents_by_id()
    {
        var provider = new FakeMemoryProvider();
        var adapter = new MemoryProviderVectorStoreAdapter(provider);

        await adapter.IndexAsync(
        [
            BuildDoc("keep", "fqn-keep"),
            BuildDoc("drop", "fqn-drop"),
        ], CancellationToken.None);

        await adapter.DeleteAsync(["drop"], CancellationToken.None);

        Assert.Equal(1, provider.Count);
    }

    [Fact]
    public async Task Roundtrip_preserves_tags_and_decorators()
    {
        var provider = new FakeMemoryProvider();
        var adapter = new MemoryProviderVectorStoreAdapter(provider);

        var doc = new VectorDocument(
            "id-x", "text-x", new ReadOnlyMemory<float>([1f, 0f]),
            new VectorMetadata
            {
                Kind = "Method",
                Language = "typescript",
                VirtualFilePath = "/tmp/x.ts",
                Fqn = "pkg.x",
                Tags = ["http-endpoint", "service"],
                Decorators = ["@Get", "@Inject"],
                HasParent = true,
                ChildCount = 3,
                InDegree = 1,
                OutDegree = 2,
            });
        await adapter.IndexAsync([doc], CancellationToken.None);

        var hits = await adapter.SearchAsync(
            "q", new ReadOnlyMemory<float>([1f, 0f]), 5, null, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("id-x", hit.Id);
        Assert.Contains("http-endpoint", hit.Metadata.Tags);
        Assert.Contains("service", hit.Metadata.Tags);
        Assert.Contains("@Get", hit.Metadata.Decorators);
        Assert.True(hit.Metadata.HasParent);
        Assert.Equal(3, hit.Metadata.ChildCount);
        Assert.Equal(1, hit.Metadata.InDegree);
        Assert.Equal(2, hit.Metadata.OutDegree);
    }

    private static VectorDocument BuildDoc(
        string id, string fqn, float[]? embedding = null, string kind = "Method")
    {
        return new VectorDocument(
            id, "text-" + id, new ReadOnlyMemory<float>(embedding ?? [1f, 0f]),
            new VectorMetadata
            {
                Kind = kind,
                Language = "typescript",
                VirtualFilePath = "/tmp/" + id + ".ts",
                Fqn = fqn,
            });
    }

    private sealed class FakeMemoryProvider : IMemoryProvider
    {
        private readonly ConcurrentDictionary<string, MemoryItem> _store = new();

        public int Count => _store.Count;

        public System.Threading.Tasks.Task StoreAsync(string key, MemoryItem item, CancellationToken ct = default)
        {
            _store[key] = item;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task<MemoryItem?> GetAsync(string key, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

        public System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchAsync(
            string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IEnumerable<MemoryItem>>(_store.Values.Take(limit));

        public System.Threading.Tasks.Task<bool> DeleteAsync(string key, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(_store.TryRemove(key, out _));

        public System.Threading.Tasks.Task ClearAsync(CancellationToken ct = default)
        {
            _store.Clear();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
            float[] queryEmbedding, int topK = 10, float minScore = 0,
            Dictionary<string, object>? filter = null, CancellationToken ct = default)
        {
            var scored = new List<ScoredMemoryItem>();
            foreach (var item in _store.Values)
            {
                if (item.Embedding is null || item.Embedding.Count != queryEmbedding.Length) continue;
                var score = CosineSimilarity(queryEmbedding, item.Embedding.ToArray());
                if (score >= minScore) scored.Add(new ScoredMemoryItem(item, score));
            }
            IReadOnlyList<ScoredMemoryItem> top = scored.OrderByDescending(s => s.Score).Take(topK).ToList();
            return System.Threading.Tasks.Task.FromResult(top);
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0, na = 0, nb = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            if (na == 0 || nb == 0) return 0;
            return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
        }
    }
}
