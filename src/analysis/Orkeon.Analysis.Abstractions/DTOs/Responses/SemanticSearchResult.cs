namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

/// <summary>The answer of <see cref="Interfaces.IRaggableStore.SemanticSearchAsync"/>: the ranked hits and what bounded them.</summary>
public sealed record SemanticSearchResult
{
    /// <summary>The hits, best first, at most <see cref="Queries.SemanticQuery.TopK"/> of them.</summary>
    public required IReadOnlyList<SearchHit> Hits { get; init; }

    /// <summary>
    /// True when more nodes matched <see cref="Queries.SemanticQuery.PreFilter"/> than the store keeps as candidates:
    /// the search then ran over the first of them only, in FQN order, and a caller must say so.
    /// </summary>
    public bool PreFilterCapped { get; init; }
}
