using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record CodebaseSearchRequest
{
    [FieldSchema(Description = "A symbol name, or words describing what the code does.", IsRequired = true)]
    public string Query { get; init; } = "";

    [FieldSchema(Description = "Levels searched: L1_Package, L2_Module, L3_Symbol, L4_Statement; empty searches every level.", ItemsType = "string", IsRequired = false)]
    public ImmutableArray<NodeLevel> Levels { get; init; } = [];

    [FieldSchema(Description = "Most hits returned (default 10); truncated says when more match.", IsRequired = false, Default = 10)]
    public int TopK { get; init; } = 10;

    [FieldSchema(Description = "Keep only these node kinds: Class, Interface, Method, Function, Property, Enum, Record, Module, Package... Empty keeps every kind.", ItemsType = "string", IsRequired = false)]
    public ImmutableArray<UniversalNodeKind> FilterKinds { get; init; } = [];

    [FieldSchema(Description = "Keep only the symbols of these packages (FQN prefixes).", ItemsType = "string", IsRequired = false)]
    public ImmutableArray<string> FilterPackages { get; init; } = [];

    [FieldSchema(Description = "Keep only the symbols of these languages (csharp, typescript, python...).", ItemsType = "string", IsRequired = false)]
    public ImmutableArray<string> FilterLanguages { get; init; } = [];

    [FieldSchema(Description = "Drop the hits scored under this value (0 keeps every hit).", IsRequired = false, Default = 0)]
    public double MinScore { get; init; }

    [FieldSchema(Description = "Return the signature of each hit.", IsRequired = false, Default = false)]
    public bool IncludeSignature { get; init; }

    /// <summary>
    /// Ranking mode: <c>Hybrid</c> (default — vector + BM25 fused by RRF),
    /// <c>Vector</c> (the pre-hybrid cosine-only behaviour), or <c>Lexical</c>.
    /// </summary>
    [FieldSchema(Description = "Ranking: Hybrid (default, vector and BM25 fused), Vector (embedding only), Lexical (BM25 only).", IsRequired = false, Default = "Hybrid")]
    public SearchMode Mode { get; init; } = SearchMode.Hybrid;
}

public sealed record CodebaseSearchResponse
{
    public required ImmutableArray<SearchHit> Hits { get; init; }
    public int TotalCandidates { get; init; }

    /// <summary>
    /// True when <c>top_k</c> hits were returned (more may exist), or when a <c>filter_*</c> or <c>levels</c>
    /// prefilter matched more nodes than it keeps, so the search did not see every node of those kinds.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Files the lazy freshness pass reindexed BEFORE answering (PLAN B3). Zero for a
    /// fresh index; non-zero says this search paid for its own freshness — an agent
    /// reading the response knows the results reflect its recent edits.
    /// </summary>
    public int RefreshedFiles { get; init; }
}
