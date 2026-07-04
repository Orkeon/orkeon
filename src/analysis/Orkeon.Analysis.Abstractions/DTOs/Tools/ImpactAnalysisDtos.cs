using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record ImpactAnalysisRequest
{
    public string Target { get; init; } = "";
    public Direction Direction { get; init; } = Direction.Backward;
    public int MaxDepth { get; init; } = 3;
    public int MaxNodes { get; init; } = 500;
}

public sealed record ImpactAnalysisResponse
{
    public required string Target { get; init; }
    public required ImmutableArray<string> DirectImpact { get; init; }
    public required ImmutableArray<string> TransitiveImpact { get; init; }
    public required ImmutableDictionary<string, int> ByPackage { get; init; }
    public required ImmutableArray<CompactNode> TopCallers { get; init; }
    public bool Truncated { get; init; }
}
