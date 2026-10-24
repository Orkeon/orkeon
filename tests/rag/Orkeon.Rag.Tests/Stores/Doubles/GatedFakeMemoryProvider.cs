using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Memory;

namespace Orkeon.Rag.Tests.Stores.Doubles;

/// <summary>
/// <see cref="FakeMemoryProvider"/> whose first <c>GetAsync</c> of a key ending with
/// <see cref="GatedSuffix"/> blocks until <see cref="Release"/> (a read-merge-write window).
/// </summary>
public sealed class GatedFakeMemoryProvider : IMemoryProvider
{
    public const string GatedSuffix = ":sources";

    private readonly FakeMemoryProvider _inner = new();
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _gatedReads;

    public Dictionary<string, MemoryItem> Items => _inner.Items;

    public void Release() => _gate.TrySetResult();

    public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default) =>
        _inner.StoreAsync(key, item, cancellationToken);

    public async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await _inner.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (key.EndsWith(GatedSuffix, StringComparison.Ordinal)
            && Interlocked.Increment(ref _gatedReads) == 1)
        {
            await _gate.Task.ConfigureAwait(false);
        }

        return result;
    }

    public Task<IEnumerable<MemoryItem>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default) =>
        _inner.SearchAsync(query, limit, filter, cancellationToken);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(key, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) => _inner.ClearAsync(cancellationToken);
}
