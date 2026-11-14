using System.Collections.Immutable;
using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record StatementQueryRequest
{
    [FieldSchema(Description = "FQNs of the symbols whose statements are listed; empty scans every indexed symbol.", ItemsType = "string", IsRequired = false)]
    public ImmutableArray<string> ParentFqns { get; init; } = [];

    [FieldSchema(Description = "Statement kinds kept: If, For, While, Return, Throw, FunctionCall, MethodCall, Await, TryCatch, Assignment, VariableDecl... Empty keeps every kind.", ItemsType = "string", IsRequired = false)]
    public ImmutableArray<StatementKind> Kinds { get; init; } = [];

    [FieldSchema(Description = "Words describing the statements wanted; ranks the hits by similarity.", IsRequired = false)]
    public string? SemanticQuery { get; init; }

    [FieldSchema(Description = "Most hits returned, at least 1 (default 50); truncated says when more exist.", IsRequired = false, Default = 50)]
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

/// <summary>Serialised in declaration order: <c>truncated</c> before the hits (#283).</summary>
public sealed record StatementQueryResponse
{
    /// <summary>True when more statements match than <c>top_k</c> returned.</summary>
    public bool Truncated { get; init; }

    public required ImmutableArray<StatementHit> Hits { get; init; }
}
