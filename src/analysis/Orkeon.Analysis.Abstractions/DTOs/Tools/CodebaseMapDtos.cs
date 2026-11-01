using System.Collections.Immutable;
using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record CodebaseMapRequest
{
    [FieldSchema(Description = "Level of the entries: L0_Monorepo, L1_Package (default), L2_Module or L3_Symbol.", IsRequired = false, Default = "L1_Package")]
    public NodeLevel Level { get; init; } = NodeLevel.L1_Package;

    [FieldSchema(Description = "Add children_count, cyclomatic and loc to each entry.", IsRequired = false, Default = false)]
    public bool IncludeMetrics { get; init; }

    [FieldSchema(Description = "Keep only the entries at or under this node (a package or folder by path, a namespace by its files and name, a type by FQN); a node id, an FQN or a path.", IsRequired = false)]
    public string? RootFqn { get; init; }

    [FieldSchema(Description = "Most entries returned (default 200); truncated says when more exist.", IsRequired = false, Default = 200)]
    public int MaxEntries { get; init; } = 200;
}

public sealed record CodebaseMapResponse
{
    public required NodeLevel Level { get; init; }

    /// <summary>True when the level holds more entries than <c>max_entries</c> returned.</summary>
    public bool Truncated { get; init; }

    public required CodebaseTotals Totals { get; init; }
    public required ImmutableArray<MapEntry> Entries { get; init; }
}

public sealed record MapEntry
{
    public required string Fqn { get; init; }
    public required string Name { get; init; }
    public required UniversalNodeKind Kind { get; init; }
    public string? Language { get; init; }
    public int? ChildrenCount { get; init; }
    public int? Cyclomatic { get; init; }
    public int? LoC { get; init; }
}

public sealed record CodebaseTotals
{
    public int NodeCount { get; init; }
    public int PackageCount { get; init; }
    public int ModuleCount { get; init; }
    public int SymbolCount { get; init; }
}
