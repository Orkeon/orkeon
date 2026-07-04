using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record StatementQueryRequest
{
    public ImmutableArray<string> ParentFqns { get; init; } = [];
    public ImmutableArray<StatementKind> Kinds { get; init; } = [];
    public string? SemanticQuery { get; init; }
    public int TopK { get; init; } = 50;
}

public sealed record StatementHit
{
    public required string ParentFqn { get; init; }
    public required string StatementId { get; init; }
    public required StatementKind Kind { get; init; }
    public required int StartLine { get; init; }
    public int? EndLine { get; init; }
    public string? Expression { get; init; }
    public string? Condition { get; init; }
    public double? Score { get; init; }
}

public sealed record StatementQueryResponse
{
    public required ImmutableArray<StatementHit> Hits { get; init; }
    public bool Truncated { get; init; }
}
