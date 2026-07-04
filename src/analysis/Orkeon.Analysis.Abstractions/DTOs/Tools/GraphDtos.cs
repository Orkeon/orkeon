using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record GraphNode
{
    public required string Fqn { get; init; }
    public required string Name { get; init; }
    public required UniversalNodeKind Kind { get; init; }
    public NodeLevel Level { get; init; }
    public bool IsSeed { get; init; }
}

public sealed record GraphEdge
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required EdgeKind Kind { get; init; }
}

public sealed record GraphMetrics(int NodeCount, int EdgeCount, int CycleCount, double AvgFanIn, double AvgFanOut);

public sealed record DependencyGraphRequest
{
    public NodeLevel Scope { get; init; } = NodeLevel.L1_Package;
    public ImmutableArray<EdgeKind> EdgeKinds { get; init; } = [EdgeKind.Imports];
    public bool IncludeExternal { get; init; }
    public string? RootFqn { get; init; }
    public int MaxNodes { get; init; } = 200;
    public bool IncludeMermaid { get; init; } = true;
    public bool IncludeDot { get; init; }
}

public sealed record DependencyGraphResponse
{
    public required ImmutableArray<GraphNode> Nodes { get; init; }
    public required ImmutableArray<GraphEdge> Edges { get; init; }
    public required GraphMetrics Metrics { get; init; }
    public string? MermaidFlowchart { get; init; }
    public string? Dot { get; init; }
    public bool Truncated { get; init; }
}

public sealed record SubGraphRequest
{
    public ImmutableArray<string> Seeds { get; init; } = [];
    public ImmutableArray<EdgeKind> EdgeKinds { get; init; } = [EdgeKind.Calls];
    public int Depth { get; init; } = 2;
    public Direction Direction { get; init; } = Direction.Forward;
    public int MaxNodes { get; init; } = 100;
    public bool IncludeMermaid { get; init; } = true;
}

public sealed record SubGraphResponse
{
    public required ImmutableArray<GraphNode> Nodes { get; init; }
    public required ImmutableArray<GraphEdge> Edges { get; init; }
    public string? MermaidFlowchart { get; init; }
    public bool Truncated { get; init; }
}
