using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core.Serialization;

internal static class NodeMapper
{
    public static NodeDto ToDto(RaggableNode node)
    {
        return new NodeDto
        {
            Id = node.Id,
            Kind = node.Kind,
            Name = node.Name,
            Fqn = node.Fqn,
            VirtualFilePath = node.VirtualFilePath,
            Range = new NodeRangeDto(
                node.Range.StartByte,
                node.Range.EndByte,
                node.Range.StartLine,
                node.Range.EndLine,
                node.Range.StartColumn),
            Level = node.Level,
            Language = node.Language,
            Signature = node.Signature,
            DocComment = node.DocComment,
            SourceSnippet = node.SourceSnippet,
            Sha256 = node.Sha256,
            SemanticSummary = node.SemanticSummary,
            EmbeddingText = node.EmbeddingText,
            Embedding = node.Embedding,
            ParentId = node.ParentId,
            ChildrenIds = [.. node.ChildrenIds],
            ImportIds = [.. node.ImportIds],
            ImportedByIds = [.. node.ImportedByIds],
            CallIds = [.. node.CallIds],
            CalledByIds = [.. node.CalledByIds],
            ExtendsIds = [.. node.ExtendsIds],
            ImplementsIds = [.. node.ImplementsIds],
            Decorators = [.. node.Decorators],
            Modifiers = [.. node.Modifiers],
            Tags = new Dictionary<string, string>(node.Tags, StringComparer.Ordinal),
            OverriddenKind = node.OverriddenKind,
            Statements = [.. node.Statements.Select(ToDto)],
            StatementsPartial = node.StatementsPartial,
            ParseStatus = node.ParseStatus,
            ParseDiagnostics = [.. node.ParseDiagnostics.Select(ToDto)],
        };
    }

    public static EdgeDto ToDto(RaggableEdge edge) => new(
        edge.Id,
        edge.FromId,
        edge.ToId,
        edge.Kind,
        edge.CallSite is null ? null : new SourceLocationDto(
            edge.CallSite.VirtualFilePath,
            edge.CallSite.StartLine,
            edge.CallSite.EndLine,
            edge.CallSite.Sha256),
        edge.Label);

    public static StatementDto ToDto(StatementNode node) => new()
    {
        Id = node.Id,
        ParentSymbolId = node.ParentSymbolId,
        Kind = node.Kind,
        StartLine = node.StartLine,
        EndLine = node.EndLine,
        Expression = node.Expression,
        Condition = node.Condition,
        References = [.. node.References.Select(r => new SymbolReferenceDto(r.Name, r.ResolvedId, r.Kind))],
        Children = [.. node.Children.Select(ToDto)],
        Depth = node.Depth,
        Embedding = node.Embedding,
    };

    public static ParseDiagnosticDto ToDto(ParseDiagnostic d) => new()
    {
        Severity = d.Severity,
        StartLine = d.StartLine,
        StartColumn = d.StartColumn,
        EndLine = d.EndLine,
        EndColumn = d.EndColumn,
        Message = d.Message,
        TreeSitterNodeType = d.TreeSitterNodeType,
    };

    public static RaggableNode ToModel(NodeDto dto)
    {
        var node = new RaggableNode
        {
            Id = dto.Id,
            Kind = dto.Kind,
            Name = dto.Name,
            VirtualFilePath = dto.VirtualFilePath,
            Range = new NodeRange(
                dto.Range.StartByte,
                dto.Range.EndByte,
                dto.Range.StartLine,
                dto.Range.EndLine,
                dto.Range.StartColumn),
            Level = dto.Level,
            Language = dto.Language,
            SourceSnippet = dto.SourceSnippet,
            Sha256 = dto.Sha256,
            Fqn = dto.Fqn,
        };
        node.Signature = dto.Signature;
        node.DocComment = dto.DocComment;
        node.SemanticSummary = dto.SemanticSummary;
        node.EmbeddingText = dto.EmbeddingText;
        node.Embedding = dto.Embedding;
        node.ParentId = dto.ParentId;
        node.ChildrenIdsMutable.AddRange(dto.ChildrenIds);
        node.ImportIdsMutable.AddRange(dto.ImportIds);
        node.ImportedByIdsMutable.AddRange(dto.ImportedByIds);
        node.CallIdsMutable.AddRange(dto.CallIds);
        node.CalledByIdsMutable.AddRange(dto.CalledByIds);
        node.ExtendsIdsMutable.AddRange(dto.ExtendsIds);
        node.ImplementsIdsMutable.AddRange(dto.ImplementsIds);
        node.DecoratorsMutable.AddRange(dto.Decorators);
        node.ModifiersMutable.AddRange(dto.Modifiers);
        foreach (var kv in dto.Tags) node.TagsMutable[kv.Key] = kv.Value;
        node.OverriddenKind = dto.OverriddenKind;
        foreach (var s in dto.Statements) node.StatementsMutable.Add(ToModel(s));
        node.StatementsPartial = dto.StatementsPartial;
        node.ParseStatus = dto.ParseStatus;
        foreach (var d in dto.ParseDiagnostics) node.ParseDiagnosticsMutable.Add(ToModel(d));
        return node;
    }

    public static RaggableEdge ToModel(EdgeDto dto) => new(
        dto.Id,
        dto.FromId,
        dto.ToId,
        dto.Kind,
        dto.CallSite is null ? null : new SourceLocation(
            dto.CallSite.VirtualFilePath,
            dto.CallSite.StartLine,
            dto.CallSite.EndLine,
            dto.CallSite.Sha256),
        dto.Label);

    public static StatementNode ToModel(StatementDto dto)
    {
        var stmt = new StatementNode
        {
            Id = dto.Id,
            ParentSymbolId = dto.ParentSymbolId,
            Kind = dto.Kind,
            StartLine = dto.StartLine,
            EndLine = dto.EndLine,
            Expression = dto.Expression,
            Condition = dto.Condition,
            Depth = dto.Depth,
        };
        stmt.Embedding = dto.Embedding;
        foreach (var r in dto.References)
            stmt.AddReference(new SymbolReference(r.Name, r.ResolvedId, r.Kind));
        foreach (var c in dto.Children)
            stmt.AddChild(ToModel(c));
        return stmt;
    }

    public static ParseDiagnostic ToModel(ParseDiagnosticDto dto) => new()
    {
        Severity = dto.Severity,
        StartLine = dto.StartLine,
        StartColumn = dto.StartColumn,
        EndLine = dto.EndLine,
        EndColumn = dto.EndColumn,
        Message = dto.Message,
        TreeSitterNodeType = dto.TreeSitterNodeType,
    };
}
