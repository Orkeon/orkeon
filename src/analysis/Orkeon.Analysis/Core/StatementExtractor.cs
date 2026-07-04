using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core;

public sealed class StatementExtractor
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal)
    {
        "statement_block", "block", "compound_statement", "function_body",
    };

    private static readonly HashSet<string> NestedDeclarationTypes = new(StringComparer.Ordinal)
    {
        // TypeScript
        "class_declaration", "abstract_class_declaration", "interface_declaration",
        "enum_declaration", "function_declaration", "method_definition",
        // Python
        "class_definition", "function_definition",
        // C#
        "method_declaration", "constructor_declaration", "destructor_declaration",
        "operator_declaration", "property_declaration", "struct_declaration",
        "record_declaration", "namespace_declaration",
        // Go
        "method_declaration_go",
        // Rust
        "function_item", "struct_item", "enum_item", "trait_item", "impl_item",
        "mod_item",
    };

    private static readonly HashSet<string> OpaqueStatementTypes = new(StringComparer.Ordinal)
    {
        "arrow_function", "function_expression", "lambda", "lambda_expression",
        "anonymous_method_expression", "closure_expression", "func_literal",
    };

    private readonly ILanguageAdapter _adapter;

    public StatementExtractor(ILanguageAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public StatementExtractionResult Extract(TsNode declarationNode, string parentSymbolId, string source)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        ArgumentNullException.ThrowIfNull(parentSymbolId);
        ArgumentNullException.ThrowIfNull(source);

        var body = FindBodyNode(declarationNode);
        if (body is null) return new StatementExtractionResult([], false);

        var roots = new List<StatementNode>();
        var context = new WalkContext(parentSymbolId, roots, source);

        foreach (var child in body.Children)
        {
            if (!child.IsNamed) continue;
            WalkInto(context, child, null, 0);
        }

        return new StatementExtractionResult(roots, context.IsPartial);
    }

    /// <summary>
    /// Carries the invariant inputs and mutable state shared across a single
    /// <see cref="WalkInto"/> traversal, replacing the previous long parameter list.
    /// </summary>
    private sealed class WalkContext
    {
        public WalkContext(string parentSymbolId, List<StatementNode> roots, string source)
        {
            ParentSymbolId = parentSymbolId;
            Roots = roots;
            Source = source;
        }

        public string ParentSymbolId { get; }
        public List<StatementNode> Roots { get; }
        public string Source { get; }
        public int Counter { get; set; }
        public bool IsPartial { get; set; }
    }

    private void WalkInto(WalkContext context, TsNode node, StatementNode? parentStmt, int depth)
    {
        if (node.IsError || node.IsMissing)
        {
            context.IsPartial = true;
            return;
        }

        if (NestedDeclarationTypes.Contains(node.Type))
        {
            return;
        }

        var kind = _adapter.MapStatementKind(node.Type);

        if (kind == StatementKind.Unknown)
        {
            WalkNamedChildren(context, node, parentStmt, depth);
            return;
        }

        var stmt = CreateStatement(context, node, kind, depth);
        CollectReferences(stmt, node);

        if (parentStmt is null) context.Roots.Add(stmt);
        else parentStmt.AddChild(stmt);

        if (OpaqueStatementTypes.Contains(node.Type))
        {
            return;
        }

        WalkNamedChildren(context, node, stmt, depth + 1);
    }

    private void WalkNamedChildren(WalkContext context, TsNode node, StatementNode? parentStmt, int depth)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsNamed) continue;
            WalkInto(context, child, parentStmt, depth);
        }
    }

    private static StatementNode CreateStatement(
        WalkContext context, TsNode node, StatementKind kind, int depth)
    {
        var parentSymbolId = context.ParentSymbolId;
        var source = context.Source;
        var startLine = node.StartPosition.Row + 1;
        var endLine = node.EndPosition.Row + 1;
        var expression = ExtractExpression(node, source);
        var condition = ExtractCondition(node);
        return new StatementNode
        {
            Id = $"{parentSymbolId}$s{context.Counter++}",
            ParentSymbolId = parentSymbolId,
            Kind = kind,
            StartLine = startLine,
            EndLine = endLine,
            Expression = expression,
            Condition = condition,
            Depth = depth,
        };
    }

    private static string ExtractExpression(TsNode node, string source)
    {
        _ = source;
        var text = node.Text ?? string.Empty;
        var firstLine = text.Split('\n', 2)[0];
        firstLine = firstLine.Trim();
        if (firstLine.Length > 200) firstLine = firstLine[..200] + "…";
        return firstLine;
    }

    private static string? ExtractCondition(TsNode node)
    {
        var conditionField = node.GetChildForField("condition");
        if (conditionField is not null)
        {
            var text = (conditionField.Text ?? string.Empty).Trim();
            if (text.StartsWith('(') && text.EndsWith(')') && text.Length >= 2)
                text = text[1..^1].Trim();
            if (text.Length > 200) text = text[..200] + "…";
            return string.IsNullOrEmpty(text) ? null : text;
        }
        return null;
    }

    private static void CollectReferences(StatementNode stmt, TsNode node)
    {
        WalkReferences(stmt, node, isRoot: true);
    }

    private static void WalkReferences(StatementNode stmt, TsNode node, bool isRoot)
    {
        if (!isRoot && OpaqueStatementTypes.Contains(node.Type)) return;

        switch (node.Type)
        {
            case "call_expression":
            case "invocation_expression":
            case "call":
            case "macro_invocation":
                CollectCallReferences(stmt, node);
                break;
            case "new_expression":
            case "object_creation_expression":
                CollectInstantiateReferences(stmt, node);
                break;
            case "assignment_expression":
            case "assignment":
            case "augmented_assignment":
            case "compound_assignment_expr":
            case "short_var_declaration":
            case "assignment_statement":
                CollectAssignmentReferences(stmt, node);
                break;
            case "variable_declarator":
            case "let_declaration":
                CollectDeclaratorReferences(stmt, node);
                break;
        }

        foreach (var child in node.Children)
        {
            if (!child.IsNamed) continue;
            WalkReferences(stmt, child, isRoot: false);
        }
    }

    private static void CollectCallReferences(StatementNode stmt, TsNode node)
    {
        var nameText = ResolveCallName(node);
        if (!string.IsNullOrEmpty(nameText))
        {
            AddReferenceUnique(stmt, nameText, ReferenceKind.Call);
        }
        var args = node.GetChildForField("arguments");
        if (args is not null)
        {
            foreach (var ident in EnumerateIdentifiers(args))
                AddReferenceUnique(stmt, ident, ReferenceKind.Read);
        }
    }

    private static void CollectInstantiateReferences(StatementNode stmt, TsNode node)
    {
        var type = node.GetChildForField("type") ?? node.GetChildForField("constructor");
        if (type is not null && !string.IsNullOrEmpty(type.Text))
        {
            AddReferenceUnique(stmt, type.Text, ReferenceKind.Instantiate);
        }
    }

    private static void CollectAssignmentReferences(StatementNode stmt, TsNode node)
    {
        var left = node.GetChildForField("left");
        if (left is not null && !string.IsNullOrEmpty(left.Text))
        {
            foreach (var ident in EnumerateIdentifiers(left))
                AddReferenceUnique(stmt, ident, ReferenceKind.Write);
        }
        var right = node.GetChildForField("right");
        if (right is not null)
        {
            foreach (var ident in EnumerateIdentifiers(right))
                AddReferenceUnique(stmt, ident, ReferenceKind.Read);
        }
    }

    private static void CollectDeclaratorReferences(StatementNode stmt, TsNode node)
    {
        var name = node.GetChildForField("name") ?? node.GetChildForField("pattern");
        if (name is not null)
        {
            foreach (var ident in EnumerateIdentifiers(name))
                AddReferenceUnique(stmt, ident, ReferenceKind.Write);
        }
        var value = node.GetChildForField("value");
        if (value is not null)
        {
            foreach (var ident in EnumerateIdentifiers(value))
                AddReferenceUnique(stmt, ident, ReferenceKind.Read);
        }
    }

    private static string ResolveCallName(TsNode callNode)
    {
        var function = callNode.GetChildForField("function")
                       ?? callNode.GetChildForField("macro")
                       ?? callNode.GetChildForField("name");
        if (function is null) return string.Empty;
        var text = (function.Text ?? string.Empty).Trim();
        if (text.Length > 120) text = text[..120];
        return text;
    }

    private static IEnumerable<string> EnumerateIdentifiers(TsNode node)
    {
        if (node.Type == "identifier" || node.Type == "type_identifier")
        {
            var t = (node.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(t)) yield return t;
            yield break;
        }
        foreach (var child in node.Children)
        {
            if (!child.IsNamed) continue;
            foreach (var ident in EnumerateIdentifiers(child))
                yield return ident;
        }
    }

    private static void AddReferenceUnique(StatementNode stmt, string name, ReferenceKind kind)
    {
        foreach (var r in stmt.References)
        {
            if (r.Name == name && r.Kind == kind) return;
        }
        stmt.AddReference(new SymbolReference(name, null, kind));
    }

    private static TsNode? FindBodyNode(TsNode declarationNode)
    {
        var bodyField = declarationNode.GetChildForField("body");
        if (bodyField is not null && BodyTypes.Contains(bodyField.Type))
            return bodyField;

        var bodyChild = declarationNode.Children.FirstOrDefault(child => BodyTypes.Contains(child.Type));
        if (bodyChild is not null) return bodyChild;

        return bodyField;
    }
}

public sealed record StatementExtractionResult(IReadOnlyList<StatementNode> Statements, bool IsPartial);
