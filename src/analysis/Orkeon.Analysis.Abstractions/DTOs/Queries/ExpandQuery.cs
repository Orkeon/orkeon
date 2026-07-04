using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record ExpandQuery
{
    public required ImmutableArray<string> Seeds { get; init; }
    public required ImmutableArray<EdgeKind> EdgeKinds { get; init; }
    public Direction Direction { get; init; } = Direction.Forward;
    public int MaxDepth { get; init; } = 3;
    public int MaxNodes { get; init; } = 500;
    public bool IncludeHierarchy { get; init; }
}
