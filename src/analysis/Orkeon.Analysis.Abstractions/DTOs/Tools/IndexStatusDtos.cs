using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record IndexStatusRequest;

public sealed record IndexStatusResponse
{
    public ImmutableList<IndexedRootDto> Roots { get; init; } = [];
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
