using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Retrieval.Doubles;

/// <summary>
/// Hand-written spy <see cref="IDocumentStore"/> for the hybrid decorator tests: records
/// every call (upserts, searches, deletions) and returns the scripted
/// <see cref="SearchResults"/> so vector rankings are fully controlled by the test.
/// </summary>
public sealed class SpyDocumentStore : IDocumentStore
{
    /// <summary>Upsert calls received, in order.</summary>
    public List<(string Collection, IReadOnlyList<EmbeddedChunk> Chunks)> Upserts { get; } = [];

    /// <summary>Search calls received, in order.</summary>
    public List<(string Collection, RetrievalQuery Query)> Searches { get; } = [];

    /// <summary>Delete-by-source calls received, in order.</summary>
    public List<(string Collection, string SourceId)> Deletes { get; } = [];

    /// <summary>Scripted result of every <see cref="SearchAsync"/> call.</summary>
    public IReadOnlyList<ScoredChunk> SearchResults { get; set; } = [];

    /// <summary>Optional exception thrown by <see cref="UpsertAsync"/> (inner validation).</summary>
    public Exception? UpsertException { get; set; }

    public Task UpsertAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (UpsertException is { } exception)
            throw exception;

        Upserts.Add((collection, chunks));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string collection,
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        Searches.Add((collection, query));
        return Task.FromResult(SearchResults);
    }

    public Task DeleteBySourceAsync(
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        Deletes.Add((collection, sourceId));
        return Task.CompletedTask;
    }
}
