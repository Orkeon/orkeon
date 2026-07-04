using Orkeon.Domain.Memory;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Interface for vector memory storage with similarity search capabilities.
/// </summary>
public interface IVectorMemoryStore
{
    /// <summary>Search Similar Async(Embedding Vector, int, float, Cancellation Token).</summary>
    System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchSimilarAsync(
        EmbeddingVector query,
        int limit,
        float threshold,
        CancellationToken cancellationToken = default);

    /// <summary>Store Async(string, Memory Item, Embedding Vector, Cancellation Token).</summary>
    System.Threading.Tasks.Task StoreAsync(
        string id,
        MemoryItem item,
        EmbeddingVector vector,
        CancellationToken cancellationToken = default);

    /// <summary>Remove Async(string, Cancellation Token).</summary>
    System.Threading.Tasks.Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>Count Async(Cancellation Token).</summary>
    System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Clear Async(Cancellation Token).</summary>
    System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default);
}
