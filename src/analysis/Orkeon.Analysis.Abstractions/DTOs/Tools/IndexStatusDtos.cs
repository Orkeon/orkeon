using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record IndexStatusRequest;

public sealed record IndexStatusResponse
{
    public ImmutableList<IndexedRootDto> Roots { get; init; } = [];

    /// <summary>How many paths are edited-but-not-reindexed (the lazy-freshness debt).</summary>
    public int DirtyCount { get; init; }

    /// <summary>A sample of the dirty paths (capped) — the stale state made observable.</summary>
    public ImmutableList<string> DirtyPaths { get; init; } = [];
}

public sealed record IndexedRootDto(
    string VirtualRoot,
    DateTimeOffset IndexedAt,
    int NodeCount,
    int EdgeCount,
    string? LanguageSummary);

public sealed record IsPathIndexedRequest
{
    public string VirtualPath { get; init; } = "";
}

public sealed record IsPathIndexedResponse
{
    public bool Indexed { get; init; }
    public string? RootParent { get; init; }
    public DateTimeOffset? IndexedAt { get; init; }
}
