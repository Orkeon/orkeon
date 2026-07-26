using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Corrective;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written scripted <see cref="IWebDocumentRetriever"/>: returns
/// <see cref="Results"/> (truncated to the requested maximum) and records every
/// call — exercises the opt-in <c>web_fallback</c> node of the corrective graph.
/// </summary>
public sealed class StubWebDocumentRetriever : IWebDocumentRetriever
{
    /// <summary>Documents returned by <see cref="SearchAsync"/> (truncated to the requested maximum).</summary>
    public List<RagDocument> Results { get; } = [];

    /// <summary>Calls received, in order.</summary>
    public List<(string Query, int MaxResults)> Calls { get; } = [];

    public Task<IReadOnlyList<RagDocument>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((query, maxResults));
        IReadOnlyList<RagDocument> results = Results.Take(maxResults).ToList();
        return Task.FromResult(results);
    }
}
