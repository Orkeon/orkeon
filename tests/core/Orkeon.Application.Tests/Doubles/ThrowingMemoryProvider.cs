using Orkeon.Domain.Memory;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryProvider"/> that is down: its writes, its searches, or both, throw
/// the configured exception — a store that refuses, a server that cannot be reached. What does not
/// fail finds nothing.
/// </summary>
internal sealed class ThrowingMemoryProvider(Exception failure, bool stores = true, bool searches = true) : IMemoryProvider
{
    public int FailedCalls { get; private set; }

    public System.Threading.Tasks.Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default) =>
        stores ? Fail<object?>() : System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult<MemoryItem?>(null);

    public System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchAsync(
        string query, int limit = 10, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default) =>
        searches ? Fail<IEnumerable<MemoryItem>>() : System.Threading.Tasks.Task.FromResult(Enumerable.Empty<MemoryItem>());

    public System.Threading.Tasks.Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        stores ? Fail<bool>() : System.Threading.Tasks.Task.FromResult(false);

    public System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding, int topK = 10, float minScore = 0.0f,
        Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default) =>
        searches
            ? Fail<IReadOnlyList<ScoredMemoryItem>>()
            : System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([]);

    private System.Threading.Tasks.Task<T> Fail<T>()
    {
        FailedCalls++;
        return System.Threading.Tasks.Task.FromException<T>(failure);
    }
}
