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
}
