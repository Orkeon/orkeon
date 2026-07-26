using Orkeon.Domain.Memory;
using Orkeon.Rag.Tests.Stores.Doubles;

namespace Orkeon.Rag.Tests.Retrieval.Doubles;

/// <summary>
/// Hand-written spy provider implementing <see cref="IHybridSearchCapable"/> on the
/// default container only (no <see cref="ICollectionAwareMemory"/>): the hybrid decorator
/// must route its native path through the interface-level overload and carry the
/// collection as a metadata filter.
/// </summary>
public class FakeHybridSearchMemoryProvider : FakeMemoryProvider, IHybridSearchCapable
{
    /// <summary>Scripted native hybrid results.</summary>
    public IReadOnlyList<ScoredMemoryItem> HybridResults { get; set; } = [];

    /// <summary>Number of interface-level hybrid calls received.</summary>
    public int InterfaceLevelCalls { get; private set; }

    /// <summary>Query text of the last hybrid call.</summary>
    public string? LastQuery { get; private set; }

    /// <summary>TopK of the last hybrid call.</summary>
    public int LastTopK { get; private set; }

    /// <summary>Filter of the last hybrid call.</summary>
    public MemoryFilter? LastFilter { get; private set; }

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        InterfaceLevelCalls++;
        LastQuery = query;
        LastTopK = topK;
        LastFilter = filter;
        return System.Threading.Tasks.Task.FromResult(HybridResults);
    }
}

/// <summary>
/// Hand-written spy provider with BOTH <see cref="ICollectionAwareMemory"/> and
/// <see cref="IHybridSearchCapable"/> including its collection-scoped overload: the
/// hybrid decorator must prefer the scoped overload (chunks live in real containers).
/// </summary>
public sealed class FakeCollectionHybridMemoryProvider
    : FakeCollectionAwareMemoryProvider, IHybridSearchCapable
{
    /// <summary>Scripted native hybrid results.</summary>
    public IReadOnlyList<ScoredMemoryItem> HybridResults { get; set; } = [];

    /// <summary>Number of interface-level (default container) hybrid calls received.</summary>
    public int InterfaceLevelCalls { get; private set; }

    /// <summary>Collection of the last scoped hybrid call.</summary>
    public string? LastHybridCollection { get; private set; }

    /// <summary>Filter of the last scoped hybrid call.</summary>
    public MemoryFilter? LastHybridFilter { get; private set; }

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        InterfaceLevelCalls++;
        return System.Threading.Tasks.Task.FromResult(HybridResults);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string collection,
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        LastHybridCollection = collection;
        LastHybridFilter = filter;
        return System.Threading.Tasks.Task.FromResult(HybridResults);
    }
}

/// <summary>
/// Collection-aware provider advertising <see cref="IHybridSearchCapable"/> but WITHOUT
/// the collection-scoped overload (the interface default throws
/// <see cref="NotSupportedException"/>): the decorator must fall back to the emulated
/// BM25 + RRF path.
/// </summary>
public sealed class FakeCollectionAwareHybridWithoutScopedProvider
    : FakeCollectionAwareMemoryProvider, IHybridSearchCapable
{
    /// <summary>Number of interface-level hybrid calls received.</summary>
    public int InterfaceLevelCalls { get; private set; }

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        InterfaceLevelCalls++;
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([]);
    }
}
