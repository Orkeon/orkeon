using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record NodeQuery
{
    public NodeLevel? Level { get; init; }
    public ImmutableArray<UniversalNodeKind>? Kinds { get; init; }
    public ImmutableArray<string>? Languages { get; init; }
    public ImmutableArray<string>? Packages { get; init; }
    public string? ParentId { get; init; }
    public ImmutableDictionary<string, string>? Tags { get; init; }
    public int? MinComplexity { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 100;
}
