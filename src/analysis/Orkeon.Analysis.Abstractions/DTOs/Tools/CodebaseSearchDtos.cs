using System.Collections.Immutable;
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
}

public sealed record CodebaseSearchResponse
{
    public required ImmutableArray<SearchHit> Hits { get; init; }
    public int TotalCandidates { get; init; }
    public bool Truncated { get; init; }
}
