namespace Orkeon.Analysis.Abstractions.Models;

public sealed class StatementNode
{
    public required string Id { get; init; }
    public required string ParentSymbolId { get; init; }
    public required StatementKind Kind { get; init; }
    public required int StartLine { get; init; }
    public int? EndLine { get; init; }
    public required string Expression { get; init; }
    public string? Condition { get; init; }
    private readonly List<SymbolReference> _references = [];
    private readonly List<StatementNode> _children = [];

    public IReadOnlyList<SymbolReference> References => _references;
    public IReadOnlyList<StatementNode> Children => _children;

    /// <summary>Appends a symbol reference discovered within this statement.</summary>
    public void AddReference(SymbolReference reference) => _references.Add(reference);

    /// <summary>Appends a nested child statement.</summary>
    public void AddChild(StatementNode child) => _children.Add(child);
    public int Depth { get; init; }
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

public record SymbolReference(string Name, string? ResolvedId, ReferenceKind Kind);
