using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Collection-scoped vector document store. Implementations must preserve
/// similarity scores end-to-end: <see cref="SearchAsync"/> returns
/// <see cref="ScoredChunk"/>s carrying the store's native scores.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Inserts or updates <paramref name="chunks"/> (keyed by chunk id) in <paramref name="collection"/>.</summary>
    Task UpsertAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>Searches <paramref name="collection"/> and returns scored candidates, best first.</summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string collection,
        RetrievalQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every chunk of <paramref name="collection"/> that originates from <paramref name="sourceId"/>.</summary>
    Task DeleteBySourceAsync(
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default);
}
