using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions;

namespace Orkeon.Analysis.Core.Serialization;

internal sealed record SerializationEnvelope
{
    public string Version { get; init; } = RaggableTreeSerializer.SupportedVersion;
    public string IndexId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public List<NodeDto> Nodes { get; init; } = [];
    public List<EdgeDto> Edges { get; init; } = [];
}

internal sealed record NodeDto
{
    public string Id { get; init; } = string.Empty;
    public UniversalNodeKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Fqn { get; init; } = string.Empty;
    public string VirtualFilePath { get; init; } = string.Empty;
    public NodeRangeDto Range { get; init; } = new(0, 0, 0, 0, 0);
    public NodeLevel Level { get; init; }
    public string Language { get; init; } = string.Empty;

    public string Signature { get; init; } = string.Empty;
    public string DocComment { get; init; } = string.Empty;
    public string SourceSnippet { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string? SemanticSummary { get; init; }
    public string EmbeddingText { get; init; } = string.Empty;

    [JsonConverter(typeof(EmbeddingJsonConverter))]
    public ReadOnlyMemory<float>? Embedding { get; init; }

    public string? ParentId { get; init; }
    public List<string> ChildrenIds { get; init; } = [];

    public List<string> ImportIds { get; init; } = [];
    public List<string> ImportedByIds { get; init; } = [];
    public List<string> CallIds { get; init; } = [];
    public List<string> CalledByIds { get; init; } = [];
    public List<string> ExtendsIds { get; init; } = [];
    public List<string> ImplementsIds { get; init; } = [];

    public List<string> Decorators { get; init; } = [];
    public List<string> Modifiers { get; init; } = [];
    public Dictionary<string, string> Tags { get; init; } = new();
    public UniversalNodeKind? OverriddenKind { get; init; }

    public List<StatementDto> Statements { get; init; } = [];
    public bool StatementsPartial { get; init; }

    public ParseStatus ParseStatus { get; init; } = ParseStatus.Ok;
    public List<ParseDiagnosticDto> ParseDiagnostics { get; init; } = [];
}

internal sealed record NodeRangeDto(int StartByte, int EndByte, int StartLine, int EndLine, int StartColumn);

internal sealed record EdgeDto(
    string Id,
    string FromId,
    string ToId,
    EdgeKind Kind,
    SourceLocationDto? CallSite,
    string? Label);

internal sealed record SourceLocationDto(string VirtualFilePath, int StartLine, int EndLine, string Sha256);

internal sealed record StatementDto
{
    public string Id { get; init; } = string.Empty;
    public string ParentSymbolId { get; init; } = string.Empty;
    public StatementKind Kind { get; init; }
    public int StartLine { get; init; }
    public int? EndLine { get; init; }
    public string Expression { get; init; } = string.Empty;
    public string? Condition { get; init; }
    public List<SymbolReferenceDto> References { get; init; } = [];
    public List<StatementDto> Children { get; init; } = [];
    public int Depth { get; init; }

    [JsonConverter(typeof(EmbeddingJsonConverter))]
    public ReadOnlyMemory<float>? Embedding { get; init; }
}

internal sealed record SymbolReferenceDto(string Name, string? ResolvedId, ReferenceKind Kind);

internal sealed record ParseDiagnosticDto
{
    public DiagnosticSeverity Severity { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? TreeSitterNodeType { get; init; }
}
