namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record SearchHit
{
    public required string Fqn { get; init; }

    /// <summary>
    /// Ranking score. Scale depends on <see cref="MatchOrigin"/>: cosine similarity for
    /// <c>vector</c>, BM25 for <c>bm25</c>, an RRF rank aggregate for <c>hybrid</c> —
    /// comparable within one response, never across origins.
    /// </summary>
    public required double Score { get; init; }

    public string? SummaryShort { get; init; }
    public string? Signature { get; init; }

    /// <summary>
    /// Which ranking(s) produced this hit: <c>"vector"</c>, <c>"bm25"</c>, or
    /// <c>"hybrid"</c> (present in both fused lists). Lets a caller see WHY a hit
    /// ranked — an exact-identifier match reads <c>bm25</c>/<c>hybrid</c>.
    /// </summary>
    public string MatchOrigin { get; init; } = "vector";
}
