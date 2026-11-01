using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record NodeQuery
{
    public NodeLevel? Level { get; init; }
    public ImmutableArray<UniversalNodeKind>? Kinds { get; init; }
    public ImmutableArray<string>? Languages { get; init; }
    public ImmutableArray<string>? Packages { get; init; }
    public string? ParentId { get; init; }

    /// <summary>
    /// When set, keeps only the nodes at or under this root: a node id, an FQN or a virtual path. A package or a
    /// file keeps the nodes under its path; a type or a member keeps itself and the FQNs under it (a <c>.</c>,
    /// <c>::</c> or <c>/</c> boundary); a namespace keeps the nodes of the files declaring it and the FQNs under its
    /// name; a text no node holds is taken as a path and as an FQN prefix, both with a boundary.
    /// </summary>
    public string? RootFqn { get; init; }
    public ImmutableDictionary<string, string>? Tags { get; init; }
    public int? MinComplexity { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 100;
}
