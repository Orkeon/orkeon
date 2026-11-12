using System.Collections.Concurrent;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryProvider"/> that keeps its entries in memory, honours a cancelled token as a
/// real store does, and runs <see cref="OnDelete"/> after each deletion: a test cancels its token there to land
/// a cancellation between two steps of a clearing.
/// </summary>
internal sealed class CancellingMemoryProvider : IMemoryProvider
{
    public ConcurrentDictionary<string, MemoryItem> Entries { get; } = new(StringComparer.Ordinal);

    public Action? OnDelete { get; set; }

    public System.Threading.Tasks.Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entries[key] = item;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(Entries.GetValueOrDefault(key));

    public System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchAsync(
        string query, int limit = 10, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult<IEnumerable<MemoryItem>>([.. Entries.Values]);

    public System.Threading.Tasks.Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var removed = Entries.TryRemove(key, out _);
        OnDelete?.Invoke();
        return System.Threading.Tasks.Task.FromResult(removed);
    }

    public System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entries.Clear();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding, int topK = 10, float minScore = 0.0f,
        Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([]);
}
