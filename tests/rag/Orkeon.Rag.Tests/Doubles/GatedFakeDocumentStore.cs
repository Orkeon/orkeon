using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IDocumentStore"/> whose <see cref="UpsertAsync"/> blocks on a gate
/// until <see cref="Release"/> is called, and records how many upserts are inside the store at
/// once (per collection and overall). Concurrency is driven by the gate, never by timing.
/// </summary>
public sealed class GatedFakeDocumentStore : IDocumentStore
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, int> _inside = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<EmbeddedChunk>> _chunks = new(StringComparer.Ordinal);

    /// <summary>Upserts currently blocked or running inside the store, all collections.</summary>
    public int TotalInside
    {
        get
        {
            lock (_sync)
            {
                return _inside.Values.Sum();
            }
        }
    }

    /// <summary>Upserts currently inside the store for one collection.</summary>
    public int InsideOf(string collection)
    {
        lock (_sync)
        {
            return _inside.GetValueOrDefault(collection);
        }
    }

    /// <summary>Chunks stored so far for one collection.</summary>
    public IReadOnlyList<EmbeddedChunk> Chunks(string collection)
    {
        lock (_sync)
        {
            return _chunks.TryGetValue(collection, out var list) ? [.. list] : [];
        }
    }

    /// <summary>Lets every blocked and future upsert proceed.</summary>
    public void Release() => _gate.TrySetResult();

    public async Task UpsertAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _inside[collection] = _inside.GetValueOrDefault(collection) + 1;
        }

        try
        {
            await _gate.Task.ConfigureAwait(false);
            lock (_sync)
            {
                if (!_chunks.TryGetValue(collection, out var list))
                {
                    _chunks[collection] = list = [];
                }

                list.AddRange(chunks);
            }
        }
        finally
        {
            lock (_sync)
            {
                _inside[collection]--;
            }
        }
    }

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string collection,
        RetrievalQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScoredChunk>>([]);

    public Task DeleteBySourceAsync(
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
