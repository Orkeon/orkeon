using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record SymbolDetailRequest
{
    public string Fqn { get; init; } = "";
    public ExpandModes Expand { get; init; } = ExpandModes.None;
    public bool IncludeSignature { get; init; } = true;
    public bool IncludeDoc { get; init; } = true;
    public bool IncludeBodyMetrics { get; init; }
    public int MaxChildren { get; init; } = 50;
}

public sealed record SymbolDetailResponse
{
    public required string Fqn { get; init; }
    public string? Name { get; init; }
    public UniversalNodeKind? Kind { get; init; }
    public NodeLevel? Level { get; init; }
    public string? Signature { get; init; }
    public string? DocComment { get; init; }
    public string? SummaryShort { get; init; }
    public BodyMetrics? Body { get; init; }
    public ImmutableArray<CompactNode> Members { get; init; } = [];
    public ImmutableArray<string> Extends { get; init; } = [];
    public ImmutableArray<string> Implements { get; init; } = [];
    public ImmutableArray<CompactNode> TopCallers { get; init; } = [];
    public ImmutableArray<CompactNode> TopCallees { get; init; } = [];
    public ImmutableArray<StatementNode> Statements { get; init; } = [];
    public bool Truncated { get; init; }
}
