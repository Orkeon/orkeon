using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Memory;

namespace Orkeon.Rag.Tests.Stores.Doubles;

/// <summary>
/// Hand-rolled <see cref="IMemoryProvider"/> double backed by a plain dictionary.
/// Implements only the base interface: <c>StoreWithEmbeddingAsync</c> and
/// <c>SearchSimilarAsync</c> resolve to the interface's default bodies (the latter returns
/// empty), which exercises the store's local-cosine fallback path.
/// </summary>
public class FakeMemoryProvider : IMemoryProvider
{
    /// <summary>Backing storage, exposed for direct inspection.</summary>
    public Dictionary<string, MemoryItem> Items { get; } = new(StringComparer.Ordinal);

    public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(item);
        Items[key] = item;
        return Task.CompletedTask;
    }

    public Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Items.TryGetValue(key, out var item) ? item : null);
    }

    public Task<IEnumerable<MemoryItem>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var results = Items.Values
            .Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
        return Task.FromResult<IEnumerable<MemoryItem>>(results);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Items.Remove(key));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        return Task.CompletedTask;
    }
}
