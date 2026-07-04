using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record ShortestPathQuery
{
    public required string From { get; init; }
    public required string To { get; init; }
    public ImmutableArray<EdgeKind> EdgeKinds { get; init; } = [EdgeKind.Calls];
    public Direction Direction { get; init; } = Direction.Forward;
}
