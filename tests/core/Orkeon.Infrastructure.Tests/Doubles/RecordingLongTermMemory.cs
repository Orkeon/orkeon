using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ILongTermMemory"/>: an in-process list that records what was added and
/// the key of every removal (GAP-30).
/// </summary>
public sealed class RecordingLongTermMemory : ILongTermMemory
{
    private readonly List<MemoryItem> _items = [];

    /// <summary>Everything added, in order.</summary>
    public List<MemoryItem> Added { get; } = [];

    /// <summary>The key of every removal, in order.</summary>
    public List<string> Removed { get; } = [];

    public Task AddAsync(MemoryItem item)
    {
        Added.Add(item);
        _items.Add(item);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10) =>
        Task.FromResult<IReadOnlyList<MemoryItem>>([.. _items.Where(i => i.Content.Contains(query, StringComparison.Ordinal)).Take(maxResults)]);

    public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding, int maxResults, float minScore, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScoredMemoryItem>>([.. _items.Take(maxResults).Select(i => new ScoredMemoryItem(i, 1f, i.Id.ToString()))]);

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Removed.Add(key);
        return Task.FromResult(_items.RemoveAll(i => i.Id.ToString() == key) > 0);
    }

    public Task ClearAsync()
    {
        _items.Clear();
        return Task.CompletedTask;
    }
}
