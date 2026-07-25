using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Security.Encryption;

namespace Orkeon.Infrastructure.Tests.Security.Encryption;

/// <summary>
/// Tests for the conditional capability forwarding of
/// <see cref="EncryptedMemoryProviderDecorator"/> (RAG-02/C4): the decorator statically
/// implements every capability but only advertises (via <see cref="IMemoryCapabilityProbe"/>)
/// and forwards those of the wrapped provider; content stays encrypted at rest while scores,
/// keys and embeddings pass through untouched.
/// </summary>
public class EncryptedMemoryProviderCapabilityTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] UnitX = [1f, 0f];
    private static readonly float[] UnitY = [0f, 1f];

    private static readonly string TestKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesEncryptionProvider CreateEncryption(bool enabled = true)
    {
        var secrets = new InMemorySecretProvider();
        secrets.SetSecret("orkeon-encryption-key", TestKeyBase64);

        return new AesEncryptionProvider(
            secrets,
            NullLogger<AesEncryptionProvider>.Instance,
            Options.Create(new AesEncryptionOptions { Enabled = enabled }));
    }

    private static EncryptedMemoryProviderDecorator CreateDecorator(IMemoryProvider inner, bool enabled = true) =>
        new(inner, CreateEncryption(enabled), NullLogger<EncryptedMemoryProviderDecorator>.Instance);

    // ── Discovery: inner WITHOUT capabilities ─────────────────────────────

    [Fact]
    public void InnerWithoutCapabilities_DecoratorDoesNotAdvertiseThem()
    {
        IMemoryProvider decorator = CreateDecorator(new FakeMemoryProvider());

        // Raw pattern matching is a false positive by construction — the documented
        // discovery path (TryGetCapability) must say no.
        Assert.True(decorator is IScoredVectorSearch);
        Assert.False(decorator.TryGetCapability<IScoredVectorSearch>(out _));
        Assert.False(decorator.TryGetCapability<IBatchUpsert>(out _));
        Assert.False(decorator.TryGetCapability<IHybridSearchCapable>(out _));
    }

    [Fact]
    public async Task InnerWithoutCapabilities_CapabilityCalls_ThrowNotSupported()
    {
        var decorator = CreateDecorator(new FakeMemoryProvider());

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decorator.SearchSimilarWithScoresAsync(UnitX, topK: 5, minScore: 0f, cancellationToken: TestCt));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decorator.UpsertBatchAsync([new MemoryUpsertEntry("k", MemoryItem.Create("x"))], TestCt));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decorator.HybridSearchAsync("query", UnitX, topK: 5, cancellationToken: TestCt));
    }

    // ── Discovery: inner WITH capabilities ────────────────────────────────

    [Fact]
    public void InnerWithCapabilities_DecoratorAdvertisesExactlyThose()
    {
        IMemoryProvider decorator = CreateDecorator(new InMemoryProvider());

        Assert.True(decorator.TryGetCapability<IScoredVectorSearch>(out _));
        Assert.True(decorator.TryGetCapability<IBatchUpsert>(out _));
        // InMemoryProvider has no native hybrid search — the decorator must not invent it.
        Assert.False(decorator.TryGetCapability<IHybridSearchCapable>(out _));
    }

    [Fact]
    public void NestedDecorators_CapabilitiesPropagateThroughTheChain()
    {
        IMemoryProvider nested = CreateDecorator(CreateDecorator(new InMemoryProvider()));

        Assert.True(nested.TryGetCapability<IScoredVectorSearch>(out _));
        Assert.False(nested.TryGetCapability<IHybridSearchCapable>(out _));
    }

    // ── Forwarding: scored search decrypts, preserves score and key ──────

    [Fact]
    public async Task SearchSimilarWithScores_DecryptsContent_PreservesScoreAndKey()
    {
        var inner = new InMemoryProvider();
        var decorator = CreateDecorator(inner);

        await decorator.StoreWithEmbeddingAsync("k-x", MemoryItem.Create("plain secret"), UnitX, TestCt);

        // At rest: content is encrypted inside the inner provider.
        var raw = await inner.GetAsync("k-x", TestCt);
        Assert.NotNull(raw);
        Assert.NotEqual("plain secret", raw.Content);

        var results = await decorator.SearchSimilarWithScoresAsync(UnitX, topK: 5, minScore: 0.5f, cancellationToken: TestCt);

        var single = Assert.Single(results);
        Assert.Equal("plain secret", single.Item.Content);
        Assert.Equal("k-x", single.Key);
        Assert.InRange(single.Score, 0.99f, 1.001f);
    }

    // ── Forwarding: batch upsert encrypts at rest ─────────────────────────

    [Fact]
    public async Task UpsertBatch_EncryptsContentAtRest_EmbeddingsStayClear()
    {
        var inner = new InMemoryProvider();
        var decorator = CreateDecorator(inner);

        await decorator.UpsertBatchAsync(
        [
            new MemoryUpsertEntry("k-1", MemoryItem.Create("first secret"), UnitX),
            new MemoryUpsertEntry("k-2", MemoryItem.Create("second secret"), UnitY),
        ], TestCt);

        var raw1 = await inner.GetAsync("k-1", TestCt);
        Assert.NotNull(raw1);
        Assert.NotEqual("first secret", raw1.Content);
        Assert.NotNull(raw1.Embedding);
        Assert.Equal(UnitX, raw1.Embedding);

        // Round-trip through the decorator restores the plaintext.
        var decrypted = await decorator.GetAsync("k-1", TestCt);
        Assert.NotNull(decrypted);
        Assert.Equal("first secret", decrypted.Content);
    }

    [Fact]
    public async Task UpsertBatch_EncryptionDisabled_DelegatesUnchanged()
    {
        var inner = new InMemoryProvider();
        var decorator = CreateDecorator(inner, enabled: false);

        await decorator.UpsertBatchAsync([new MemoryUpsertEntry("k-1", MemoryItem.Create("plaintext"), UnitX)], TestCt);

        var raw = await inner.GetAsync("k-1", TestCt);
        Assert.NotNull(raw);
        Assert.Equal("plaintext", raw.Content);
    }

    [Fact]
    public async Task UpsertBatch_ThenScoredSearch_FullEncryptedRoundTrip()
    {
        var decorator = CreateDecorator(new InMemoryProvider());

        await decorator.UpsertBatchAsync(
        [
            new MemoryUpsertEntry("k-x", MemoryItem.Create("x secret"), UnitX),
            new MemoryUpsertEntry("k-y", MemoryItem.Create("y secret"), UnitY),
        ], TestCt);

        var results = await decorator.SearchSimilarWithScoresAsync(UnitY, topK: 5, minScore: 0.5f, cancellationToken: TestCt);

        var single = Assert.Single(results);
        Assert.Equal("y secret", single.Item.Content);
        Assert.Equal("k-y", single.Key);
    }

    // ── ICollectionAwareMemory forwarding (RAG-03/C2) ─────────────────────

    /// <summary>
    /// Hand-rolled collection-aware inner provider: per-collection dictionaries plus a
    /// bare <see cref="IMemoryProvider"/> surface, used to prove conditional forwarding
    /// and encryption-at-rest through the decorator.
    /// </summary>
    private sealed class FakeCollectionAwareProvider : IMemoryProvider, ICollectionAwareMemory
    {
        public Dictionary<string, Dictionary<string, MemoryItem>> Collections { get; } = new(StringComparer.Ordinal);

        // Bare IMemoryProvider surface (unused by the collection-scoped tests).
        public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<MemoryItem?>(null);

        public Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<MemoryItem>>([]);

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public MemoryFilter? LastDeleteFilter { get; private set; }

        public List<string> DroppedCollections { get; } = [];

        public Task StoreWithEmbeddingAsync(
            string collection, string key, MemoryItem item, ReadOnlyMemory<float> embedding,
            CancellationToken cancellationToken = default)
        {
            Bucket(collection)[key] = Copy(item, embedding.ToArray());
            return Task.CompletedTask;
        }

        public Task UpsertBatchAsync(
            string collection, IReadOnlyList<MemoryUpsertEntry> entries,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in entries)
                Bucket(collection)[entry.Key] = Copy(entry.Item, entry.Embedding?.ToArray());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
            string collection, ReadOnlyMemory<float> embedding, int topK, float minScore,
            MemoryFilter? filter = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ScoredMemoryItem> results = Bucket(collection)
                .Select(pair => new ScoredMemoryItem(pair.Value, 0.9f, pair.Key))
                .Take(topK)
                .ToList();
            return Task.FromResult(results);
        }

        public Task DeleteByFilterAsync(
            string collection, MemoryFilter filter, CancellationToken cancellationToken = default)
        {
            LastDeleteFilter = filter;
            var bucket = Bucket(collection);
            foreach (var key in bucket.Where(p => filter.Matches(p.Value)).Select(p => p.Key).ToList())
                bucket.Remove(key);
            return Task.CompletedTask;
        }

        public Task DropCollectionAsync(string collection, CancellationToken cancellationToken = default)
        {
            DroppedCollections.Add(collection);
            Collections.Remove(collection);
            return Task.CompletedTask;
        }

        private Dictionary<string, MemoryItem> Bucket(string collection)
        {
            if (!Collections.TryGetValue(collection, out var bucket))
            {
                bucket = new Dictionary<string, MemoryItem>(StringComparer.Ordinal);
                Collections[collection] = bucket;
            }

            return bucket;
        }

        private static MemoryItem Copy(MemoryItem item, float[]? embedding) =>
            MemoryItem.Create(
                item.Content,
                embedding ?? item.Embedding,
                item.Importance,
                item.Source,
                item.Tags,
                customProperties: item.Metadata.CustomProperties is { } custom ? new(custom) : null);
    }

    [Fact]
    public void CollectionAwareMemory_InnerWithout_NotAdvertised_AndCallsThrow()
    {
        IMemoryProvider decorator = CreateDecorator(new FakeMemoryProvider());

        Assert.True(decorator is ICollectionAwareMemory);
        Assert.False(decorator.TryGetCapability<ICollectionAwareMemory>(out _));
    }

    [Fact]
    public async Task CollectionAwareMemory_InnerWithout_CallsThrowNotSupported()
    {
        var decorator = CreateDecorator(new FakeMemoryProvider());

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decorator.UpsertBatchAsync("docs", [new MemoryUpsertEntry("k", MemoryItem.Create("x"))], TestCt));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decorator.SearchSimilarWithScoresAsync("docs", UnitX, topK: 5, minScore: 0f, cancellationToken: TestCt));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decorator.DeleteByFilterAsync("docs", new MemoryFilter { Source = "s" }, TestCt));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decorator.DropCollectionAsync("docs", TestCt));
    }

    [Fact]
    public void CollectionAwareMemory_InnerWith_IsAdvertised()
    {
        IMemoryProvider decorator = CreateDecorator(new FakeCollectionAwareProvider());

        Assert.True(decorator.TryGetCapability<ICollectionAwareMemory>(out _));
        // Absent capabilities of the inner are still not invented.
        Assert.False(decorator.TryGetCapability<IHybridSearchCapable>(out _));
    }

    [Fact]
    public async Task CollectionAwareMemory_UpsertBatch_EncryptsAtRest_AndSearchDecrypts()
    {
        var inner = new FakeCollectionAwareProvider();
        var decorator = CreateDecorator(inner);

        await decorator.UpsertBatchAsync("docs",
        [
            new MemoryUpsertEntry(
                "chunk-1",
                MemoryItem.Create("plain chunk", customProperties: new Dictionary<string, string> { ["rag.kind"] = "chunk" }),
                UnitX),
        ], TestCt);

        // At rest: encrypted content, clear embedding, custom properties preserved.
        var raw = inner.Collections["docs"]["chunk-1"];
        Assert.NotEqual("plain chunk", raw.Content);
        Assert.Equal(UnitX, raw.Embedding);
        Assert.Equal("chunk", raw.Metadata.CustomProperties!["rag.kind"]);

        // Round trip: decrypted content, score/key/custom properties preserved.
        var results = await decorator.SearchSimilarWithScoresAsync("docs", UnitX, topK: 5, minScore: 0f, cancellationToken: TestCt);
        var single = Assert.Single(results);
        Assert.Equal("plain chunk", single.Item.Content);
        Assert.Equal("chunk-1", single.Key);
        Assert.Equal("chunk", single.Item.Metadata.CustomProperties!["rag.kind"]);
    }

    [Fact]
    public async Task CollectionAwareMemory_DeleteByFilterAndDrop_ForwardUnchanged()
    {
        var inner = new FakeCollectionAwareProvider();
        var decorator = CreateDecorator(inner);

        await decorator.StoreWithEmbeddingAsync("docs", "k-1", MemoryItem.Create("x", source: "drop.md"), UnitX, TestCt);
        await decorator.DeleteByFilterAsync("docs", new MemoryFilter { Source = "drop.md" }, TestCt);

        Assert.Equal("drop.md", inner.LastDeleteFilter!.Source);
        Assert.Empty(inner.Collections["docs"]);

        await decorator.DropCollectionAsync("docs", TestCt);
        Assert.Equal(["docs"], inner.DroppedCollections);
    }
}
