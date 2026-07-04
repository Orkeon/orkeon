using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record CodebaseMapRequest
{
    public NodeLevel Level { get; init; } = NodeLevel.L1_Package;
    public bool IncludeMetrics { get; init; }
    public string? RootFqn { get; init; }
    public int MaxEntries { get; init; } = 200;
}

public sealed record CodebaseMapResponse
{
    public required NodeLevel Level { get; init; }
    public required ImmutableArray<MapEntry> Entries { get; init; }
    public required CodebaseTotals Totals { get; init; }
    public bool Truncated { get; init; }
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
