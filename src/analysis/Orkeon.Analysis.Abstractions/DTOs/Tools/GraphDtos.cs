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

/// <summary>
/// Metrics of a dependency graph. <see cref="NodeCount"/>, <see cref="EdgeCount"/> and the fan figures cover the
/// nodes returned (selected by <c>max_nodes</c>, <c>root_fqn</c> and <c>node_kinds</c>); <see cref="CycleCount"/>
/// covers the whole scope.
/// </summary>
public sealed record GraphMetrics(int NodeCount, int EdgeCount, int CycleCount, double AvgFanIn, double AvgFanOut);

/// <summary>One cycle (a strongly connected component) of a dependency graph, over the whole scope.</summary>
public sealed record GraphCycle
{
    /// <summary>The members, as FQNs (the form of <see cref="GraphNode.Fqn"/>), sorted, capped by <c>max_cycle_members</c>.</summary>
    [JsonPropertyName("fqns")]
    public required ImmutableArray<string> Fqns { get; init; }

    /// <summary>The number of members before the cap.</summary>
    [JsonPropertyName("member_count")]
    public required int MemberCount { get; init; }

    /// <summary>
    /// The edge kinds followed to find the cycle: every kind of <c>edge_kinds</c>, imports when it is empty. On the wire,
    /// the names <c>edge_kinds</c> takes, joined by <c>", "</c> (<c>imports</c>, <c>imports, calls</c>).
    /// </summary>
    [JsonPropertyName("edge_kind")]
    public required EdgeKind EdgeKind { get; init; }
}

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

    [FieldSchema(Description = "Most cycles returned, the largest first. cycle_count keeps the full count.", IsRequired = false)]
    [JsonPropertyName("max_cycles")]
    public int MaxCycles { get; init; } = 20;

    [FieldSchema(Description = "Most members returned per cycle. member_count keeps the full size.", IsRequired = false)]
    [JsonPropertyName("max_cycle_members")]
    public int MaxCycleMembers { get; init; } = 50;

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

    /// <summary>The cycles of the whole scope (not only of the nodes returned), the largest first.</summary>
    [JsonPropertyName("cycles")]
    public ImmutableArray<GraphCycle> Cycles { get; init; } = [];

    /// <summary>True when <see cref="Cycles"/> was cut by <c>max_cycles</c>, or a cycle's members by <c>max_cycle_members</c>.</summary>
    [JsonPropertyName("cycles_truncated")]
    public bool CyclesTruncated { get; init; }
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
