using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record CodebaseSearchRequest
{
    public string Query { get; init; } = "";
    public ImmutableArray<NodeLevel> Levels { get; init; } = [];
    public int TopK { get; init; } = 10;
    public ImmutableArray<UniversalNodeKind> FilterKinds { get; init; } = [];
    public ImmutableArray<string> FilterPackages { get; init; } = [];
    public ImmutableArray<string> FilterLanguages { get; init; } = [];
    public double MinScore { get; init; }
    public bool IncludeSignature { get; init; }

    /// <summary>
    /// Ranking mode: <c>Hybrid</c> (default — vector + BM25 fused by RRF),
    /// <c>Vector</c> (the pre-hybrid cosine-only behaviour), or <c>Lexical</c>.
    /// </summary>
    public SearchMode Mode { get; init; } = SearchMode.Hybrid;
}

public sealed record CodebaseSearchResponse
{
    public required ImmutableArray<SearchHit> Hits { get; init; }
    public int TotalCandidates { get; init; }
    public bool Truncated { get; init; }

    /// <summary>
    /// Files the lazy freshness pass reindexed BEFORE answering (PLAN B3). Zero for a
    /// fresh index; non-zero says this search paid for its own freshness — an agent
    /// reading the response knows the results reflect its recent edits.
    /// </summary>
    public int RefreshedFiles { get; init; }
}
