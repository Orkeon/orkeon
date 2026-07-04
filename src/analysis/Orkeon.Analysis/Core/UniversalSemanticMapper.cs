using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.TreeSitter;
using TsLanguage = TreeSitter.Language;
using TsNode = TreeSitter.Node;
using TsQuery = TreeSitter.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Analysis.Core;

public sealed partial class UniversalSemanticMapper
{
    private static readonly HashSet<string> ModifierTypes = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "static", "async", "abstract",
        "readonly", "export", "default", "override", "virtual", "sealed",
        "internal", "partial"
    };

    private static readonly HashSet<UniversalNodeKind> ExecutableKinds =
    [
        UniversalNodeKind.Function,
        UniversalNodeKind.Method,
        UniversalNodeKind.Constructor,
        UniversalNodeKind.Destructor,
        UniversalNodeKind.Operator,
    ];

    private readonly ILanguageAdapter _adapter;
    private readonly TreeSitterParserPool _pool;
    private readonly StatementExtractor _statementExtractor;
    private readonly bool _extractStatements;
    private readonly ILogger<UniversalSemanticMapper> _logger;

    public UniversalSemanticMapper(ILanguageAdapter adapter, TreeSitterParserPool pool)
        : this(adapter, pool, extractStatements: true) { }

    public UniversalSemanticMapper(ILanguageAdapter adapter, TreeSitterParserPool pool, bool extractStatements)
        : this(adapter, pool, extractStatements, logger: null) { }

    public UniversalSemanticMapper(
        ILanguageAdapter adapter,
        TreeSitterParserPool pool,
        bool extractStatements,
        ILogger<UniversalSemanticMapper>? logger)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _statementExtractor = new StatementExtractor(_adapter);
        _extractStatements = extractStatements;
        _logger = logger ?? NullLogger<UniversalSemanticMapper>.Instance;
    }

    public IReadOnlyList<RaggableNode> ExtractNodes(string filePath, string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sourceCode);

        using var parsed = _pool.Parse(sourceCode, _adapter.LanguageName);
        var root = parsed.Root;

        var moduleNode = BuildModuleNode(filePath, sourceCode, root, parsed.Diagnostics, parsed.Diagnostics.Length > 0);
        var nodes = new List<RaggableNode> { moduleNode };

        var decls = RunQuery(parsed.Tree.Language, _adapter.DeclarationQuery, root, "decl");
        var docs = RunQuery(parsed.Tree.Language, _adapter.DocCommentQuery, root, "doc");
        var decorators = RunQuery(parsed.Tree.Language, _adapter.DecoratorQuery, root, "decorator");

        var symbolNodes = new List<(TsNode TsNode, RaggableNode Node)>();
        foreach (var tsNode in decls.OrderBy(n => n.StartIndex))
        {
            var symbol = BuildSymbolNode(filePath, sourceCode, tsNode);
            if (symbol is null) continue;
            AttachDecorators(symbol, tsNode, decorators);
            AttachDocComment(symbol, tsNode, docs, sourceCode);
            AttachModifiers(symbol, tsNode);
            nodes.Add(symbol);
            symbolNodes.Add((tsNode, symbol));
        }

        LinkHierarchy(moduleNode, symbolNodes);
        ComputeFqns(filePath, moduleNode, symbolNodes);

        if (_extractStatements)
        {
            ExtractStatementsInto(sourceCode, symbolNodes);
        }

        return nodes;
    }

    private void ExtractStatementsInto(
        string sourceCode, List<(TsNode TsNode, RaggableNode Node)> symbolNodes)
    {
        foreach (var (tsNode, node) in symbolNodes)
        {
            if (!ExecutableKinds.Contains(node.Kind)) continue;
            var result = _statementExtractor.Extract(tsNode, node.Fqn, sourceCode);
            if (result.Statements.Count == 0 && !result.IsPartial) continue;
            foreach (var s in result.Statements) node.StatementsMutable.Add(s);
            node.StatementsPartial = result.IsPartial;
        }
    }

    private RaggableNode BuildModuleNode(
        string filePath, string source, TsNode root, System.Collections.Immutable.ImmutableArray<ParseDiagnostic> diagnostics, bool hasErrors)
    {
        var sha = ComputeSha256(source);
        var range = ToRange(root);
        var module = new RaggableNode
        {
            Id = $"{filePath}#module@0",
            Kind = UniversalNodeKind.Module,
            Name = Path.GetFileName(filePath),
            VirtualFilePath = filePath,
            Range = range,
            Level = NodeLevel.L2_Module,
            Language = _adapter.LanguageName,
            SourceSnippet = source,
            Sha256 = sha,
        };
        module.Fqn = filePath;
        if (!hasErrors)
        {
            module.ParseStatus = ParseStatus.Ok;
        }
        else
        {
            module.ParseStatus = diagnostics.Length == 0 ? ParseStatus.Failed : ParseStatus.Partial;
        }
        foreach (var d in diagnostics) module.ParseDiagnosticsMutable.Add(d);
        return module;
    }

    private RaggableNode? BuildSymbolNode(string filePath, string source, TsNode tsNode)
    {
        if (tsNode.IsError || tsNode.IsMissing) return null;
        var kind = _adapter.MapNodeKind(tsNode.Type);
        if (kind == UniversalNodeKind.Unknown) return null;
        kind = _adapter.RefineKind(kind, tsNode);
        if (kind == UniversalNodeKind.Unknown) return null;

        var name = ResolveName(tsNode);
        if (string.IsNullOrEmpty(name)) return null;

        var slice = SafeSlice(source, tsNode.StartIndex, tsNode.EndIndex);
        var sha = ComputeSha256(slice);
        var signature = _adapter.ExtractSignature(tsNode, source);

        return new RaggableNode
        {
            Id = $"{filePath}#{name}@{tsNode.StartIndex}",
            Kind = kind,
            Name = name,
            VirtualFilePath = filePath,
            Range = ToRange(tsNode),
            Level = NodeLevel.L3_Symbol,
            Language = _adapter.LanguageName,
            SourceSnippet = slice,
            Sha256 = sha,
            Signature = signature,
        };
    }

    private string ResolveName(TsNode node)
    {
        var adapterName = _adapter.ExtractName(node);
        if (!string.IsNullOrEmpty(adapterName)) return adapterName;

        var nameChild = node.GetChildForField("name");
        if (nameChild is not null && !string.IsNullOrEmpty(nameChild.Text)) return nameChild.Text;

        if (node.Type == "lexical_declaration")
        {
            return ResolveLexicalDeclarationName(node);
        }

        if (node.Type == "variable_declarator")
        {
            var inner = node.GetChildForField("name");
            if (inner is not null && !string.IsNullOrEmpty(inner.Text)) return inner.Text;
        }

        return string.Empty;
    }

    private static string ResolveLexicalDeclarationName(TsNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Type != "variable_declarator") continue;
            var inner = child.GetChildForField("name");
            if (inner is not null && !string.IsNullOrEmpty(inner.Text)) return inner.Text;
        }
        return string.Empty;
    }

    private static void AttachDecorators(
        RaggableNode node, TsNode declNode, IReadOnlyList<TsNode> decorators)
    {
        foreach (var d in decorators)
        {
            var inside = d.StartIndex >= declNode.StartIndex && d.EndIndex <= declNode.EndIndex;
            if (inside)
            {
                if (IsDirectChildDecorator(d, declNode)) AddDecorator(node, d);
                continue;
            }
            if (d.StartIndex >= declNode.StartIndex) continue;
            if (d.EndIndex < declNode.StartIndex - 200) continue;
            if (IsAdjacentBefore(d, declNode)) AddDecorator(node, d);
        }
    }

    private static void AddDecorator(RaggableNode node, TsNode decorator)
    {
        var text = (decorator.Text ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(text) && !node.Decorators.Contains(text))
            node.DecoratorsMutable.Add(text);
    }

    private static bool IsDirectChildDecorator(TsNode decorator, TsNode declNode)
    {
        var parent = decorator.Parent;
        return parent is not null && parent.StartIndex == declNode.StartIndex
            && parent.EndIndex == declNode.EndIndex
            && parent.Type == declNode.Type;
    }

    private void AttachDocComment(
        RaggableNode node, TsNode declNode, IReadOnlyList<TsNode> docs, string source)
    {
        var adapterResult = _adapter.ResolveDocComment(declNode, docs, source);
        if (adapterResult is not null)
        {
            node.DocComment = adapterResult;
            return;
        }

        var anchor = UnwrapAnchor(declNode);
        var ordered = docs.Where(d => d.EndIndex <= anchor.StartIndex)
            .OrderBy(d => d.StartIndex)
            .ToList();
        if (ordered.Count == 0) return;

        var collected = new List<string>();
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var d = ordered[i];
            var anchorForWalk = i == ordered.Count - 1 ? anchor : ordered[i + 1];
            if (!IsAdjacentBefore(d, anchorForWalk)) break;
            collected.Insert(0, (d.Text ?? string.Empty).Trim());
        }

        if (collected.Count == 0) return;
        node.DocComment = string.Join('\n', collected);
    }

    private static TsNode UnwrapAnchor(TsNode node)
    {
        var parent = node.Parent;
        return parent is not null && parent.Type == "export_statement" ? parent : node;
    }

    private void AttachModifiers(RaggableNode node, TsNode tsNode)
    {
        AddDeclarationModifiers(node, tsNode);
        AddExportModifiers(node, tsNode.Parent);

        foreach (var extra in _adapter.GetExtraModifiers(tsNode, node.Name))
        {
            AddModifierIfAbsent(node, extra);
        }
    }

    private static void AddDeclarationModifiers(RaggableNode node, TsNode tsNode)
    {
        foreach (var child in tsNode.Children)
        {
            var type = child.Type;
            if (ModifierTypes.Contains(type) || type.EndsWith("_modifier", StringComparison.Ordinal))
            {
                AddModifierIfAbsent(node, (child.Text ?? type).Trim());
            }
        }
    }

    private static void AddExportModifiers(RaggableNode node, TsNode? parent)
    {
        if (parent is null || parent.Type != "export_statement")
            return;

        AddModifierIfAbsent(node, "export");
        foreach (var child in parent.Children)
        {
            if (child.Type == "default")
                AddModifierIfAbsent(node, "default");
        }
    }

    private static void AddModifierIfAbsent(RaggableNode node, string? modifier)
    {
        if (!string.IsNullOrEmpty(modifier) && !node.Modifiers.Contains(modifier))
        {
            node.ModifiersMutable.Add(modifier);
        }
    }

    private static void LinkHierarchy(
        RaggableNode module,
        List<(TsNode TsNode, RaggableNode Node)> symbolNodes)
    {
        foreach (var (tsNode, node) in symbolNodes)
        {
            var parentNode = FindEnclosingParent(node, symbolNodes);

            if (parentNode is not null)
            {
                node.ParentId = parentNode.Id;
                parentNode.ChildrenIdsMutable.Add(node.Id);
            }
            else
            {
                node.ParentId = module.Id;
                module.ChildrenIdsMutable.Add(node.Id);
            }
            _ = tsNode;
        }
    }

    private static RaggableNode? FindEnclosingParent(
        RaggableNode node,
        List<(TsNode TsNode, RaggableNode Node)> symbolNodes)
    {
        RaggableNode? parentNode = null;
        var parentStart = -1;
        foreach (var (_, other) in symbolNodes)
        {
            if (ReferenceEquals(node, other)) continue;
            if (other.Range.StartByte <= node.Range.StartByte
                && other.Range.EndByte >= node.Range.EndByte
                && other.Range.StartByte > parentStart)
            {
                parentStart = other.Range.StartByte;
                parentNode = other;
            }
        }
        return parentNode;
    }

    private static void ComputeFqns(
        string filePath,
        RaggableNode module,
        List<(TsNode TsNode, RaggableNode Node)> symbolNodes)
    {
        var byId = symbolNodes.ToDictionary(p => p.Node.Id, p => p.Node);
        foreach (var (_, node) in symbolNodes)
        {
            var chain = new List<string> { node.Name };
            var current = node.ParentId;
            while (current is not null && byId.TryGetValue(current, out var parent))
            {
                chain.Add(parent.Name);
                current = parent.ParentId;
            }
            chain.Reverse();
            node.Fqn = $"{filePath}::{string.Join("::", chain)}";
        }
        module.Fqn = filePath;
    }

    private static bool IsAdjacentBefore(TsNode earlier, TsNode later)
    {
        var gapStart = earlier.EndIndex;
        var gapEnd = later.StartIndex;
        if (gapEnd <= gapStart) return false;
        var sibling = earlier.NextSibling;
        while (sibling is not null && sibling.StartIndex < later.StartIndex)
        {
            if (sibling.StartIndex == later.StartIndex && sibling.EndIndex == later.EndIndex) return true;
            if (sibling.IsNamed && !IsSkippableBetween(sibling.Type)) return false;
            sibling = sibling.NextSibling;
        }
        return true;
    }

    private static bool IsSkippableBetween(string type)
    {
        if (type.Contains("comment", StringComparison.Ordinal)) return true;
        if (type.StartsWith("decorator", StringComparison.Ordinal)) return true;
        if (type.StartsWith("attribute", StringComparison.Ordinal)) return true;
        return false;
    }

    private List<TsNode> RunQuery(TsLanguage language, string queryText, TsNode root, string captureName)
    {
        if (string.IsNullOrWhiteSpace(queryText)) return [];
        TsQuery query;
        try { query = language.CreateQuery(queryText); }
        catch (InvalidOperationException ex)
        {
            // The tree-sitter API raises InvalidOperationException when a query
            // cannot be compiled (syntax/structure error). We intentionally degrade
            // to "no captures" so indexing continues, but trace it so a lacunar graph
            // is diagnosable instead of silent.
            LogQueryCompilationFailed(ex, _adapter.LanguageName, captureName);
            return [];
        }
        var results = new List<TsNode>();
        using (query)
        {
            using var cursor = query.Execute(root);
            foreach (var match in cursor.Matches)
            {
                foreach (var capture in match.Captures)
                {
                    if (capture.Name == captureName)
                    {
                        results.Add(capture.Node);
                    }
                }
            }
        }
        return results;
    }

    private static NodeRange ToRange(TsNode node) => new(
        node.StartIndex,
        node.EndIndex,
        node.StartPosition.Row + 1,
        node.EndPosition.Row + 1,
        node.StartPosition.Column + 1);

    private static string SafeSlice(string source, int startIndex, int endIndex)
    {
        if (startIndex < 0) startIndex = 0;
        if (endIndex > source.Length) endIndex = source.Length;
        if (startIndex >= endIndex) return string.Empty;
        return source[startIndex..endIndex];
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Tree-sitter query compilation failed for language '{Language}' (capture '{CaptureName}'); skipping these captures, the resulting graph may be incomplete.")]
    private partial void LogQueryCompilationFailed(Exception ex, string language, string captureName);
}
