using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

/// <summary>The request of <c>edge_list</c>: every edge of the given kinds in the whole index, one page at a time.</summary>
public sealed record EdgeListRequest
{
    /// <summary>The default number of edges per page.</summary>
    public const int DefaultPageSize = 200;

    /// <summary>The most edges per page.</summary>
    public const int MaxPageSize = 1000;

    [FieldSchema(Description = "Edge kinds to list, by name: contains, calls, imports, inherits, tests, documents, project_depends. Legacy enum names (Imports, Extends, Implements...) are still accepted. inherits lists both extends and implements edges.", ItemsType = "string", IsRequired = true)]
    [JsonPropertyName("edge_kinds")]
    public ImmutableArray<string> EdgeKinds { get; init; } = [];

    [FieldSchema(Description = "Keep only the edges with an end at or under this node: a package or file (by path), a namespace (its files and name), a type or member (by FQN); a node id, an FQN or a path.", IsRequired = false)]
    [JsonPropertyName("root_fqn")]
    public string? RootFqn { get; init; }

    [FieldSchema(Description = "The next_cursor of the previous page; omit it for the first page.", IsRequired = false)]
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [FieldSchema(Description = "Edges per page, 1 to 1000 (default 200).", IsRequired = false)]
    [JsonPropertyName("page_size")]
    public int PageSize { get; init; } = DefaultPageSize;
}

/// <summary>One edge of <c>edge_list</c>: its two ends as FQNs (and node ids) and its kind.</summary>
public sealed record EdgeListItem
{
    /// <summary>The source FQN.</summary>
    [JsonPropertyName("from")]
    public required string From { get; init; }

    /// <summary>The target FQN (an external or unresolved target keeps its own name).</summary>
    [JsonPropertyName("to")]
    public required string To { get; init; }

    /// <summary>The edge kind: <c>extends</c> or <c>implements</c> for an <c>inherits</c> edge, never <c>inherits</c>.</summary>
    [JsonPropertyName("kind")]
    public required EdgeKind Kind { get; init; }

    /// <summary>The source node id.</summary>
    [JsonPropertyName("from_id")]
    public required string FromId { get; init; }

    /// <summary>The target node id.</summary>
    [JsonPropertyName("to_id")]
    public required string ToId { get; init; }
}

/// <summary>The response of <c>edge_list</c>, serialised in declaration order: the total and the cursor before the page (#283).</summary>
public sealed record EdgeListResponse
{
    /// <summary>How many edges match the kinds and the root over the whole index, every page counted.</summary>
    [JsonPropertyName("total")]
    public required int Total { get; init; }

    /// <summary>The cursor of the next page, to pass back as <c>cursor</c>; null on the last page.</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    /// <summary>The edges of this page, ordered by source FQN, then target FQN, then kind.</summary>
    [JsonPropertyName("edges")]
    public required ImmutableArray<EdgeListItem> Edges { get; init; }
}
