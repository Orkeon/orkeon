using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

/// <summary>
/// The position of an edge in the order <see cref="Interfaces.IRaggableStore.ListEdgesAsync"/> walks: source FQN,
/// target FQN, kind, then edge id, each compared ordinally (the kind by value).
/// </summary>
public sealed record EdgeListKey(string FromFqn, string ToFqn, EdgeKind Kind, string EdgeId);

/// <summary>One edge of an <see cref="EdgeListPage"/>, its two ends given as node ids and as FQNs.</summary>
public sealed record ListedEdge
{
    /// <summary>The edge id.</summary>
    public required string Id { get; init; }

    /// <summary>The source node id.</summary>
    public required string FromId { get; init; }

    /// <summary>The source FQN; the id itself when no node holds it.</summary>
    public required string FromFqn { get; init; }

    /// <summary>The target node id.</summary>
    public required string ToId { get; init; }

    /// <summary>The target FQN; the id itself when no node holds it (an external or unresolved target).</summary>
    public required string ToFqn { get; init; }

    /// <summary>The edge kind.</summary>
    public required EdgeKind Kind { get; init; }

    /// <summary>The position of this edge in the listing order.</summary>
    public EdgeListKey Key => new(FromFqn, ToFqn, Kind, Id);
}

/// <summary>One page of <see cref="Interfaces.IRaggableStore.ListEdgesAsync"/>.</summary>
public sealed record EdgeListPage
{
    /// <summary>The edges of the page, in listing order.</summary>
    public required ImmutableArray<ListedEdge> Edges { get; init; }

    /// <summary>How many edges match the kinds and the root, over the whole store (not only this page).</summary>
    public required int Total { get; init; }

    /// <summary>True when edges follow the last one of this page.</summary>
    public bool HasMore { get; init; }
}
