using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record PackageSummaryRequest
{
    public string Fqn { get; init; } = "";
    public bool IncludePublicExports { get; init; } = true;
    public bool IncludeDependencies { get; init; } = true;
    public int MaxExports { get; init; } = 50;
}

public sealed record PackageSummaryResponse
{
    public required string Fqn { get; init; }
    public string? Name { get; init; }
    public int FileCount { get; init; }
    public int SymbolCount { get; init; }
    public ImmutableArray<string> EntryPoints { get; init; } = [];
    public ImmutableArray<string> PublicExports { get; init; } = [];
    public ImmutableArray<string> Dependencies { get; init; } = [];
    public string? SummaryShort { get; init; }
    public bool Truncated { get; init; }
}
