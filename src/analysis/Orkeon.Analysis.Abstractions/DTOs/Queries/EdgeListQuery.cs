using Orkeon.Analysis.Abstractions.DTOs.Responses;

namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

/// <summary>
/// One page of every edge of the store whose kind is in <see cref="Kinds"/>, in the order of
/// <see cref="EdgeListKey"/> (source FQN, target FQN, kind, edge id; ordinal), so that a page resumes where the
/// previous one stopped.
/// </summary>
public sealed record EdgeListQuery
{
    /// <summary>The kinds kept, as a mask; <see cref="EdgeKind.None"/> keeps every kind.</summary>
    public EdgeKind Kinds { get; init; } = EdgeKind.None;

    /// <summary>
    /// When set, keeps only an edge with an end at or under this root, resolved as <see cref="NodeQuery.RootFqn"/>
    /// is: a package or a file by path, a type or a member by FQN with a boundary, a namespace by its files and its
    /// name as an FQN prefix, an unknown text as a path and as an FQN prefix with a boundary.
    /// </summary>
    public string? RootFqn { get; init; }

    /// <summary>The key of the last edge of the previous page; null starts at the first edge.</summary>
    public EdgeListKey? After { get; init; }

    /// <summary>The most edges returned; the store caps it.</summary>
    public int Take { get; init; } = 200;
}
