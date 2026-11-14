using System.Collections.Immutable;
using Orkeon.Domain.Attributes;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record ImpactAnalysisRequest
{
    [FieldSchema(Description = "FQN of the symbol about to change.", IsRequired = true)]
    public string Target { get; init; } = "";

    [FieldSchema(Description = "Backward (default) lists what depends on target; Forward what target depends on; Both.", IsRequired = false, Default = "Backward")]
    public Direction Direction { get; init; } = Direction.Backward;

    [FieldSchema(Description = "Most hops followed for the transitive impact, at least 1 (default 3).", IsRequired = false, Default = 3)]
    public int MaxDepth { get; init; } = 3;

    [FieldSchema(Description = "Most symbols visited, at least 1 (default 500); truncated says when the walk stopped early.", IsRequired = false, Default = 500)]
    public int MaxNodes { get; init; } = 500;
}

public sealed record ImpactAnalysisResponse
{
    public required string Target { get; init; }

    /// <summary>True when the expansion stopped at <c>max_nodes</c>: the impact lists are not complete.</summary>
    public bool Truncated { get; init; }

    public required ImmutableDictionary<string, int> ByPackage { get; init; }
    public required ImmutableArray<string> DirectImpact { get; init; }
    public required ImmutableArray<string> TransitiveImpact { get; init; }
    public required ImmutableArray<CompactNode> TopCallers { get; init; }
}
