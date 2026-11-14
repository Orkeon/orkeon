using System.Collections.Immutable;
using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record PackageSummaryRequest
{
    [FieldSchema(Description = "FQN of the package (an entry of codebase_map at L1_Package).", IsRequired = true)]
    public string Fqn { get; init; } = "";

    [FieldSchema(Description = "Return the public exports of the package.", IsRequired = false, Default = true)]
    public bool IncludePublicExports { get; init; } = true;

    [FieldSchema(Description = "Return the packages this one depends on.", IsRequired = false, Default = true)]
    public bool IncludeDependencies { get; init; } = true;

    [FieldSchema(Description = "Most exports returned, at least 1 (default 50); truncated says when more exist.", IsRequired = false, Default = 50)]
    public int MaxExports { get; init; } = 50;
}

public sealed record PackageSummaryResponse
{
    public required string Fqn { get; init; }
    public string? Name { get; init; }

    /// <summary>True when a list below was cut at <c>max_items</c>.</summary>
    public bool Truncated { get; init; }

    public int FileCount { get; init; }
    public int SymbolCount { get; init; }
    public ImmutableArray<string> EntryPoints { get; init; } = [];
    public ImmutableArray<string> PublicExports { get; init; } = [];
    public ImmutableArray<string> Dependencies { get; init; } = [];
    public string? SummaryShort { get; init; }
}
