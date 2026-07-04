namespace Orkeon.Analysis.Abstractions.Models;

public sealed class RaggableNode
{
    public required string Id { get; init; }
    public required UniversalNodeKind Kind { get; init; }
    public required string Name { get; init; }
    public string Fqn { get; set; } = string.Empty;
    public required string VirtualFilePath { get; init; }
    public required NodeRange Range { get; init; }
    public required NodeLevel Level { get; init; }
    public required string Language { get; init; }

    public string Signature { get; set; } = string.Empty;
    public string DocComment { get; set; } = string.Empty;
    public required string SourceSnippet { get; init; }
    public required string Sha256 { get; init; }
    public string? SemanticSummary { get; set; }
    public string EmbeddingText { get; set; } = string.Empty;
    public ReadOnlyMemory<float>? Embedding { get; set; }

    public string? ParentId { get; set; }

    // Public API exposes read-only views (CA1002); mutation stays internal to the
    // RaggableTree pipeline assemblies via InternalsVisibleTo.
    private readonly List<string> _childrenIds = [];
    private readonly List<string> _importIds = [];
    private readonly List<string> _importedByIds = [];
    private readonly List<string> _callIds = [];
    private readonly List<string> _calledByIds = [];
    private readonly List<string> _extendsIds = [];
    private readonly List<string> _implementsIds = [];
    private readonly List<string> _decorators = [];
    private readonly List<string> _modifiers = [];
    private readonly Dictionary<string, string> _tags = new();
    private readonly List<StatementNode> _statements = [];
    private readonly List<ParseDiagnostic> _parseDiagnostics = [];

    public IReadOnlyList<string> ChildrenIds => _childrenIds;
    public IReadOnlyList<string> ImportIds => _importIds;
    public IReadOnlyList<string> ImportedByIds => _importedByIds;
    public IReadOnlyList<string> CallIds => _callIds;
    public IReadOnlyList<string> CalledByIds => _calledByIds;
    public IReadOnlyList<string> ExtendsIds => _extendsIds;
    public IReadOnlyList<string> ImplementsIds => _implementsIds;
    public IReadOnlyList<string> Decorators => _decorators;
    public IReadOnlyList<string> Modifiers => _modifiers;
    public IReadOnlyDictionary<string, string> Tags => _tags;

    internal List<string> ChildrenIdsMutable => _childrenIds;
    internal List<string> ImportIdsMutable => _importIds;
    internal List<string> ImportedByIdsMutable => _importedByIds;
    internal List<string> CallIdsMutable => _callIds;
    internal List<string> CalledByIdsMutable => _calledByIds;
    internal List<string> ExtendsIdsMutable => _extendsIds;
    internal List<string> ImplementsIdsMutable => _implementsIds;
    internal List<string> DecoratorsMutable => _decorators;
    internal List<string> ModifiersMutable => _modifiers;
    internal Dictionary<string, string> TagsMutable => _tags;
    internal List<StatementNode> StatementsMutable => _statements;
    internal List<ParseDiagnostic> ParseDiagnosticsMutable => _parseDiagnostics;

    public UniversalNodeKind? OverriddenKind { get; set; }
    public UniversalNodeKind EffectiveKind => OverriddenKind ?? Kind;

    public IReadOnlyList<StatementNode> Statements => _statements;
    public bool StatementsPartial { get; set; }
    public BodyMetrics? Body => _statements.Count > 0 ? BodyMetrics.Compute(_statements, StatementsPartial) : null;

    public ParseStatus ParseStatus { get; set; } = ParseStatus.Ok;
    public IReadOnlyList<ParseDiagnostic> ParseDiagnostics => _parseDiagnostics;
}

public record NodeRange(int StartByte, int EndByte, int StartLine, int EndLine, int StartColumn);
