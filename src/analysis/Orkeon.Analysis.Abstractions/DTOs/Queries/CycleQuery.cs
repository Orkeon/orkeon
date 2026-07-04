using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record CycleQuery
{
    public NodeLevel Scope { get; init; } = NodeLevel.L3_Symbol;
    public ImmutableArray<EdgeKind> EdgeKinds { get; init; } = [EdgeKind.Calls];
    public int MinLength { get; init; } = 2;
}
