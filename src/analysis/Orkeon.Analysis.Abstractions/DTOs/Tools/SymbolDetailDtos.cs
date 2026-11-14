using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record SymbolDetailRequest
{
    [FieldSchema(Description = "FQN of the symbol (a hit of codebase_search).", IsRequired = true)]
    public string Fqn { get; init; } = "";

    [FieldSchema(Description = "What to expand, one name or several joined by commas: Members, Inheritance (extends, implements, derived_by, implemented_by), Callers, Callees, PublicExports, Statements, Decorators, ParentChain, All; None by default.", IsRequired = false, Default = "None")]
    public ExpandModes Expand { get; init; } = ExpandModes.None;

    [FieldSchema(Description = "Return the signature.", IsRequired = false, Default = true)]
    public bool IncludeSignature { get; init; } = true;

    [FieldSchema(Description = "Return the doc comment.", IsRequired = false, Default = true)]
    public bool IncludeDoc { get; init; } = true;

    [FieldSchema(Description = "Return the body metrics (lines, cyclomatic complexity).", IsRequired = false, Default = false)]
    public bool IncludeBodyMetrics { get; init; }

    [FieldSchema(Description = "Most members returned under Members, 1 to 200 (default 50); truncated says when more exist.", IsRequired = false, Default = 50)]
    public int MaxChildren { get; init; } = 50;
}

public sealed record SymbolDetailResponse
{
    public required string Fqn { get; init; }
    public string? Name { get; init; }
    public UniversalNodeKind? Kind { get; init; }
    public NodeLevel? Level { get; init; }

    /// <summary>True when <see cref="Members"/> was cut at <c>max_children</c>.</summary>
    public bool Truncated { get; init; }

    public string? Signature { get; init; }
    public string? DocComment { get; init; }
    public string? SummaryShort { get; init; }
    public BodyMetrics? Body { get; init; }
    public ImmutableArray<CompactNode> Members { get; init; } = [];
    /// <summary>Under <c>expand: inheritance</c>, the FQNs this symbol extends.</summary>
    public ImmutableArray<string> Extends { get; init; } = [];

    /// <summary>Under <c>expand: inheritance</c>, the FQNs this symbol implements.</summary>
    public ImmutableArray<string> Implements { get; init; } = [];

    /// <summary>Under <c>expand: inheritance</c>, the FQNs of the symbols that extend this one.</summary>
    [JsonPropertyName("derived_by")]
    public ImmutableArray<string> DerivedBy { get; init; } = [];

    /// <summary>Under <c>expand: inheritance</c>, the FQNs of the symbols that implement this one.</summary>
    [JsonPropertyName("implemented_by")]
    public ImmutableArray<string> ImplementedBy { get; init; } = [];
    public ImmutableArray<CompactNode> TopCallers { get; init; } = [];
    public ImmutableArray<CompactNode> TopCallees { get; init; } = [];
    public ImmutableArray<StatementNode> Statements { get; init; } = [];
}
