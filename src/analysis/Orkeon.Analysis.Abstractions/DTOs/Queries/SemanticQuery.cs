namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

/// <summary>How <c>SemanticSearchAsync</c> ranks candidates.</summary>
public enum SearchMode
{
    /// <summary>
    /// Vector + BM25 fused by Reciprocal Rank Fusion — the default. Concept queries keep
    /// the embedding's recall; exact-identifier queries gain the lexical index's
    /// precision (pure cosine misses <c>getUserById</c> when the query says "fetch user",
    /// and vice-versa).
    /// </summary>
    Hybrid,

    /// <summary>Embedding cosine only — the pre-hybrid behaviour, kept for pinning.</summary>
    Vector,

    /// <summary>BM25 only — also the automatic degradation when no embedder is wired.</summary>
    Lexical,
}

public sealed record SemanticQuery
{
    public required string Text { get; init; }
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Minimum score, applied to the VECTOR candidates before fusion (cosine scale).
    /// RRF-fused scores are rank aggregates on another scale, so the floor cannot apply
    /// to them; lexical-only results ignore it.
    /// </summary>
    public double MinScore { get; init; }

    public NodeQuery? PreFilter { get; init; }

    /// <summary>Ranking mode. Defaults to <see cref="SearchMode.Hybrid"/>.</summary>
    public SearchMode Mode { get; init; } = SearchMode.Hybrid;
}
