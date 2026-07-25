using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written scripted <see cref="IDocumentStore"/>: <see cref="SearchAsync"/>
/// returns exactly the <see cref="ScoredChunk"/>s configured per collection in
/// <see cref="ResultsByCollection"/> (empty list otherwise) and records every call.
/// Unlike the shared <c>FakeDocumentStore</c>, scores are fully controlled by the
/// test — required to exercise MinScore filtering and budget truncation.
/// </summary>
public sealed class StubDocumentStore : IDocumentStore
{
    /// <summary>Scripted search results, keyed by collection name.</summary>
    public Dictionary<string, IReadOnlyList<ScoredChunk>> ResultsByCollection { get; } =
        new(StringComparer.Ordinal);

    /// <summary>Search calls received, in order.</summary>
    public List<(string Collection, RetrievalQuery Query)> SearchCalls { get; } = [];

    public Task UpsertAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string collection,
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        SearchCalls.Add((collection, query));
        var results = ResultsByCollection.TryGetValue(collection, out var scripted)
            ? scripted
            : [];
        return Task.FromResult(results);
    }

    public Task DeleteBySourceAsync(
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
