using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record Cycle
{
    public required ImmutableArray<string> Fqns { get; init; }
    public required EdgeKind EdgeKind { get; init; }
}
