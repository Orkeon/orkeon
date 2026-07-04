using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DTOs.Responses;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record FlowTraceRequest
{
    public string From { get; init; } = "";
    public string? To { get; init; }
    public Direction Direction { get; init; } = Direction.Forward;
    public int MaxDepth { get; init; } = 4;
    public ImmutableArray<EdgeKind> EdgeKinds { get; init; } = [EdgeKind.Calls];
    public bool IncludeAllPaths { get; init; }
    public int MaxPaths { get; init; } = 20;
}

public sealed record FlowTraceResponse
{
    public required ImmutableArray<CallPath> Paths { get; init; }
    public bool Truncated { get; init; }
}
