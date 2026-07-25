using Orkeon.Domain.Memory;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Tests.Memory;

/// <summary>
/// Tests for the capability discovery contract (RAG-02/C4):
/// <see cref="MemoryCapabilityExtensions.TryGetCapability{TCapability}"/> combined with
/// <see cref="IMemoryCapabilityProbe"/> for decorators.
/// </summary>
public class MemoryCapabilityExtensionsTests
{
    /// <summary>Minimal hand-written provider without any optional capability.</summary>
    private class StubMemoryProvider : IMemoryProvider
    {
        public System.Threading.Tasks.Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult<MemoryItem?>(null);

        public System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult<IEnumerable<MemoryItem>>([]);

        public System.Threading.Tasks.Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(false);

        public System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Hand-written provider genuinely implementing <see cref="IScoredVectorSearch"/>.</summary>
    private sealed class StubScoredVectorProvider : StubMemoryProvider, IScoredVectorSearch
    {
        public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
            ReadOnlyMemory<float> embedding,
            int topK,
            float minScore,
            MemoryFilter? filter = null,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([]);
    }

    /// <summary>
    /// Hand-written decorator-style double: statically implements the capability but
    /// reports its effective availability through <see cref="IMemoryCapabilityProbe"/>.
    /// </summary>
    private sealed class StubProbedProvider : StubMemoryProvider, IScoredVectorSearch, IMemoryCapabilityProbe
    {
        public bool Advertise { get; set; }

        public bool HasCapability<TCapability>() where TCapability : class => Advertise;

        public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
            ReadOnlyMemory<float> embedding,
            int topK,
            float minScore,
            MemoryFilter? filter = null,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([]);
    }

    /// <summary>Hand-written provider genuinely implementing <see cref="ICollectionAwareMemory"/>.</summary>
    private sealed class StubCollectionAwareProvider : StubMemoryProvider, ICollectionAwareMemory
    {
        public System.Threading.Tasks.Task StoreWithEmbeddingAsync(
            string collection, string key, MemoryItem item, ReadOnlyMemory<float> embedding,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task UpsertBatchAsync(
            string collection, IReadOnlyList<MemoryUpsertEntry> entries,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
            string collection, ReadOnlyMemory<float> embedding, int topK, float minScore,
            MemoryFilter? filter = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([]);

        public System.Threading.Tasks.Task DeleteByFilterAsync(
            string collection, MemoryFilter filter, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task DropCollectionAsync(
            string collection, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    [Fact]
    public void TryGetCapability_NullProvider_Throws()
    {
        IMemoryProvider provider = null!;
        Assert.Throws<ArgumentNullException>(() => provider.TryGetCapability<IScoredVectorSearch>(out _));
    }

    [Fact]
    public void TryGetCapability_ProviderWithoutCapability_ReturnsFalse()
    {
        var provider = new StubMemoryProvider();

        Assert.False(provider.TryGetCapability<IScoredVectorSearch>(out var capability));
        Assert.Null(capability);
    }

    [Fact]
    public void TryGetCapability_ProviderWithCapability_ReturnsInstance()
    {
        var provider = new StubScoredVectorProvider();

        Assert.True(provider.TryGetCapability<IScoredVectorSearch>(out var capability));
        Assert.Same(provider, capability);
    }

    [Fact]
    public void TryGetCapability_ProbeDenies_ReturnsFalse_EvenThoughInterfaceIsImplemented()
    {
        var provider = new StubProbedProvider { Advertise = false };

        // Raw pattern matching would say yes — the probe must win.
        Assert.True(provider is IScoredVectorSearch);
        Assert.False(provider.TryGetCapability<IScoredVectorSearch>(out var capability));
        Assert.Null(capability);
    }

    [Fact]
    public void TryGetCapability_ProbeConfirms_ReturnsInstance()
    {
        var provider = new StubProbedProvider { Advertise = true };

        Assert.True(provider.TryGetCapability<IScoredVectorSearch>(out var capability));
        Assert.Same(provider, capability);
    }

    [Fact]
    public void TryGetCapability_CollectionAwareMemory_DiscoveredLikeAnyCapability()
    {
        var bare = new StubMemoryProvider();
        var collectionAware = new StubCollectionAwareProvider();

        Assert.False(bare.TryGetCapability<ICollectionAwareMemory>(out _));
        Assert.True(collectionAware.TryGetCapability<ICollectionAwareMemory>(out var capability));
        Assert.Same(collectionAware, capability);
    }
}
