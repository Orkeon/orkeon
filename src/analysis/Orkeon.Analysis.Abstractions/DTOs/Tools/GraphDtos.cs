using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

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
    [FieldSchema(Description = "Edge kinds to follow, by name: contains, calls, imports, inherits, tests, documents, project_depends. Legacy enum names (Imports, Extends, Implements...) are still accepted.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("edge_kinds")]
    public ImmutableArray<string> EdgeKinds { get; init; } = ["imports"];

    [FieldSchema(Description = "Restrict nodes to these kinds, by name: file, project, namespace, type, member, doc_section, test. Empty keeps every node of the scope.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("node_kinds")]
    public ImmutableArray<string> NodeKinds { get; init; } = [];
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
    [FieldSchema(Description = "Edge kinds to follow, by name: contains, calls, imports, inherits, tests, documents, project_depends. Legacy enum names (Imports, Extends, Implements...) are still accepted.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("edge_kinds")]
    public ImmutableArray<string> EdgeKinds { get; init; } = ["calls"];

    [FieldSchema(Description = "Keep only expanded nodes of these kinds, by name: file, project, namespace, type, member, doc_section, test. Empty keeps every node.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("node_kinds")]
    public ImmutableArray<string> NodeKinds { get; init; } = [];
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
