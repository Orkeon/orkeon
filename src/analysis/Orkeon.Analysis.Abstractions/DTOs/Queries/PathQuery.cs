using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record PathQuery
{
    public required string From { get; init; }
    public string? To { get; init; }
    public ImmutableArray<EdgeKind> EdgeKinds { get; init; } = [EdgeKind.Calls];
    public Direction Direction { get; init; } = Direction.Forward;
    public Func<RaggableNode, bool>? LeafPredicate { get; init; }
    public int MaxDepth { get; init; } = 5;
    public int MaxPaths { get; init; } = 50;
}
