using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.TreeSitter;
using TsLanguage = TreeSitter.Language;
using TsNode = TreeSitter.Node;
using TsQuery = TreeSitter.Query;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Builds the cross-node dependency edge set of a RaggableTree.
/// </summary>
public sealed class DependencyGraphBuilder
{
    private readonly ILanguageAdapter _adapter;
    private readonly TreeSitterParserPool _pool;
    private readonly Dictionary<string, FileContext> _files =
        new(StringComparer.OrdinalIgnoreCase);

    public DependencyGraphBuilder(ILanguageAdapter adapter, TreeSitterParserPool pool)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    public void RegisterFile(string filePath, string source, IReadOnlyList<RaggableNode> nodes)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(nodes);
        _files[filePath] = new FileContext(source, nodes);
    }

    public Task<IReadOnlyList<RaggableEdge>> BuildDependenciesAsync(
        IReferenceResolver resolver, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return BuildDependenciesCoreAsync(resolver, ct);
    }

    private async Task<IReadOnlyList<RaggableEdge>> BuildDependenciesCoreAsync(
        IReferenceResolver resolver, CancellationToken ct)
    {
        var index = BuildGlobalIndex();
        var edges = new List<RaggableEdge>();
        var unresolvedCalls = new List<UnresolvedRef>();

        foreach (var (filePath, ctx) in _files)
        {
            ct.ThrowIfCancellationRequested();
            var moduleNode = ctx.Nodes.FirstOrDefault(n => n.Level == NodeLevel.L2_Module);
            if (moduleNode is null) continue;

            CollectFileEdges(edges, unresolvedCalls, index, filePath, moduleNode, ctx);
        }

        var resolved = await resolver.ResolveAsync(unresolvedCalls, index, ct).ConfigureAwait(false);
        edges.AddRange(resolved);

        ApplyInverseLinks(edges, index);
        return edges;
    }

    private void CollectFileEdges(
        List<RaggableEdge> edges,
        List<UnresolvedRef> unresolvedCalls,
        Dictionary<string, RaggableNode> index,
        string filePath,
        RaggableNode moduleNode,
        FileContext ctx)
    {
        using var parsed = _pool.Parse(ctx.Source, _adapter.LanguageName);
        var language = parsed.Tree.Language;

        CollectImports(edges, index, filePath, moduleNode, parsed.Root, language);
        CollectInheritance(edges, index, ctx, parsed.Root, language);
        CollectCalls(unresolvedCalls, ctx, parsed.Root, language);
    }

    private void CollectImports(
        List<RaggableEdge> edges,
        Dictionary<string, RaggableNode> index,
        string filePath,
        RaggableNode moduleNode,
        TsNode root,
        TsLanguage language)
    {
        var importNodes = RunQuery(language, _adapter.ImportQuery, root, "import");
        foreach (var importNode in importNodes)
        {
            var spec = ExtractImportSpecifier(importNode);
            if (spec is null) continue;

            var resolved = _adapter.ResolveImportPath(spec, filePath);
            if (resolved is null) continue;

            var target = ResolveImportTarget(resolved, index);
            if (target is null) continue;

            var edgeId = $"{moduleNode.Id}→{target.Id}:{EdgeKind.Imports}";
            if (edges.Any(e => e.Id == edgeId)) continue;
            if (moduleNode.Id == target.Id) continue;
            edges.Add(new RaggableEdge(edgeId, moduleNode.Id, target.Id, EdgeKind.Imports));
        }
    }

    private static RaggableNode? ResolveImportTarget(
        string resolved, Dictionary<string, RaggableNode> index)
    {
        if (resolved.StartsWith("ext::", StringComparison.Ordinal))
        {
            if (!index.TryGetValue(resolved, out var external))
            {
                external = CreateExternalNode(resolved);
                index[resolved] = external;
            }
            return external;
        }

        // Try the adapter-resolved path first, then probe extension variants in the
        // in-memory index. This is required for virtual paths (VFS v2.2+) because
        // TypeScriptAdapter.ResolveImportPath used File.Exists to pick the right
        // extension, but File.Exists always returns false for virtual paths, so the
        // fallback hardcoded ".ts". Now we probe the index directly.
        return ResolveInIndex(resolved, index);
    }

    private void CollectInheritance(
        List<RaggableEdge> edges,
        Dictionary<string, RaggableNode> index,
        FileContext ctx,
        TsNode root,
        TsLanguage language)
    {
        var byShortName = BuildShortNameIndex(index);

        foreach (var (captureName, kind) in new[] { ("extends", EdgeKind.Extends), ("implements", EdgeKind.Implements) })
        {
            var clauses = RunQuery(language, _adapter.InheritanceQuery, root, captureName);
            foreach (var clause in clauses)
            {
                var ownerSymbol = FindEnclosingSymbol(clause, ctx.Nodes);
                if (ownerSymbol is null) continue;
                foreach (var name in ExtractTypeNames(clause))
                {
                    var target = ResolveByShortName(name, byShortName, ownerSymbol.VirtualFilePath);
                    if (target is null) continue;
                    var edgeId = $"{ownerSymbol.Id}→{target.Id}:{kind}";
                    if (edges.Any(e => e.Id == edgeId)) continue;
                    if (ownerSymbol.Id == target.Id) continue;
                    edges.Add(new RaggableEdge(edgeId, ownerSymbol.Id, target.Id, kind));
                }
            }
        }
    }

    private void CollectCalls(
        List<UnresolvedRef> unresolved,
        FileContext ctx,
        TsNode root,
        TsLanguage language)
    {
        var calls = RunQuery(language, _adapter.CallQuery, root, "call");
        foreach (var call in calls)
        {
            var ownerSymbol = FindEnclosingSymbol(call, ctx.Nodes);
            if (ownerSymbol is null) continue;
            var name = ExtractCallName(call);
            if (string.IsNullOrEmpty(name)) continue;

            var location = new SourceLocation(
                ownerSymbol.VirtualFilePath,
                call.StartPosition.Row + 1,
                call.EndPosition.Row + 1,
                ComputeSha(call.Text ?? string.Empty));

            var kind = call.Type == "new_expression" ? ReferenceKind.Instantiate : ReferenceKind.Call;
            unresolved.Add(new UnresolvedRef(ownerSymbol.Fqn, name, kind, location));
        }
    }

    private static void ApplyInverseLinks(
        List<RaggableEdge> edges,
        Dictionary<string, RaggableNode> index)
    {
        foreach (var edge in edges)
        {
            if (!index.TryGetValue(edge.FromId, out var fromNode))
                fromNode = index.Values.FirstOrDefault(n => n.Id == edge.FromId);
            if (!index.TryGetValue(edge.ToId, out var toNode))
                toNode = index.Values.FirstOrDefault(n => n.Id == edge.ToId);

            if (fromNode is null || toNode is null) continue;

            LinkNodes(edge.Kind, fromNode, toNode);
        }
    }

    private static void LinkNodes(EdgeKind kind, RaggableNode fromNode, RaggableNode toNode)
    {
        switch (kind)
        {
            case EdgeKind.Imports:
                if (!fromNode.ImportIds.Contains(toNode.Id)) fromNode.ImportIdsMutable.Add(toNode.Id);
                if (!toNode.ImportedByIds.Contains(fromNode.Id)) toNode.ImportedByIdsMutable.Add(fromNode.Id);
                break;
            case EdgeKind.Calls:
                if (!fromNode.CallIds.Contains(toNode.Id)) fromNode.CallIdsMutable.Add(toNode.Id);
                if (!toNode.CalledByIds.Contains(fromNode.Id)) toNode.CalledByIdsMutable.Add(fromNode.Id);
                break;
            case EdgeKind.Extends:
                if (!fromNode.ExtendsIds.Contains(toNode.Id)) fromNode.ExtendsIdsMutable.Add(toNode.Id);
                break;
            case EdgeKind.Implements:
                if (!fromNode.ImplementsIds.Contains(toNode.Id)) fromNode.ImplementsIdsMutable.Add(toNode.Id);
                break;
        }
    }

    private Dictionary<string, RaggableNode> BuildGlobalIndex()
    {
        var index = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        foreach (var (filePath, ctx) in _files)
        {
            // Adapters return import-resolved paths normalized to forward slashes.
            // Physical Windows paths registered with backslashes must be normalized here
            // so the index keys line up with the adapter's lookup keys.
            var normalizedFilePath = filePath.Replace('\\', '/');
            foreach (var node in ctx.Nodes)
            {
                if (!string.IsNullOrEmpty(node.Fqn)) index[node.Fqn] = node;
                index[node.Id] = node;
                if (node.Level == NodeLevel.L2_Module)
                {
                    index[normalizedFilePath] = node;
                    if (!string.Equals(filePath, normalizedFilePath, StringComparison.Ordinal))
                        index[filePath] = node;
                }
            }
        }
        return index;
    }

    private static Dictionary<string, List<RaggableNode>> BuildShortNameIndex(
        Dictionary<string, RaggableNode> index)
    {
        var map = new Dictionary<string, List<RaggableNode>>(StringComparer.Ordinal);
        foreach (var node in index.Values)
        {
            if (node.Level != NodeLevel.L3_Symbol) continue;
            if (!map.TryGetValue(node.Name, out var bucket))
            {
                bucket = [];
                map[node.Name] = bucket;
            }
            if (!bucket.Contains(node)) bucket.Add(node);
        }
        return map;
    }

    private static RaggableNode? ResolveByShortName(
        string name,
        Dictionary<string, List<RaggableNode>> index,
        string currentVirtualFilePath)
    {
        if (!index.TryGetValue(name, out var candidates) || candidates.Count == 0) return null;
        return candidates.FirstOrDefault(c => c.VirtualFilePath == currentVirtualFilePath) ?? candidates[0];
    }

    private static RaggableNode? FindEnclosingSymbol(TsNode target, IReadOnlyList<RaggableNode> nodes)
    {
        RaggableNode? best = null;
        var start = target.StartIndex;
        var end = target.EndIndex;
        foreach (var node in nodes)
        {
            if (node.Level != NodeLevel.L3_Symbol) continue;
            if (node.Range.StartByte <= start && node.Range.EndByte >= end
                && (best is null || node.Range.StartByte > best.Range.StartByte))
            {
                best = node;
            }
        }
        return best;
    }

    private static string? ExtractImportSpecifier(TsNode importNode)
    {
        foreach (var descendant in Descend(importNode))
        {
            if (descendant.Type == "string" || descendant.Type == "string_fragment")
            {
                var text = descendant.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        return null;
    }

    private static IEnumerable<string> ExtractTypeNames(TsNode clause)
    {
        foreach (var descendant in Descend(clause))
        {
            if (descendant.Type == "identifier" || descendant.Type == "type_identifier")
            {
                var text = descendant.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(text)) yield return text;
            }
        }
    }

    private static string ExtractCallName(TsNode call)
    {
        var funcChild = call.GetChildForField("function")
            ?? call.GetChildForField("constructor")
            ?? call.GetChildForField("callee");
        if (funcChild is not null)
        {
            return ShortNameOf(funcChild);
        }
        if (call.Children.Count > 0) return ShortNameOf(call.Children[0]);
        return string.Empty;
    }

    private static string ShortNameOf(TsNode node)
    {
        if (node.Type == "member_expression" || node.Type == "subscript_expression")
        {
            var prop = node.GetChildForField("property");
            if (prop is not null) return prop.Text ?? string.Empty;
        }
        if (node.Type == "identifier" || node.Type == "property_identifier" || node.Type == "type_identifier")
        {
            return node.Text ?? string.Empty;
        }
        var last = node.LastNamedChild;
        return last?.Text ?? node.Text ?? string.Empty;
    }

    private static IEnumerable<TsNode> Descend(TsNode node)
    {
        var stack = new Stack<TsNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            var children = current.Children;
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort query compilation: a malformed/unsupported tree-sitter query yields no matches rather than crashing edge resolution.")]
    private static List<TsNode> RunQuery(
        TsLanguage language, string queryText, TsNode root, string captureName)
    {
        if (string.IsNullOrWhiteSpace(queryText)) return [];
        TsQuery query;
        try { query = language.CreateQuery(queryText); }
        catch { return []; }
        var results = new List<TsNode>();
        using (query)
        {
            using var cursor = query.Execute(root);
            foreach (var match in cursor.Matches)
            {
                foreach (var capture in match.Captures)
                {
                    if (capture.Name == captureName) results.Add(capture.Node);
                }
            }
        }
        return results;
    }

    /// <summary>
    /// Resolves a local import path to a module node by probing the in-memory index with
    /// multiple extension candidates. This replaces the old File.Exists approach used in
    /// ILanguageAdapter.ResolveImportPath, which broke under VFS v2.2 because virtual paths
    /// are never present on the physical disk.
    ///
    /// Probe order mirrors TypeScript module resolution:
    ///   1. Exact path as returned by the adapter (e.g. already has .ts)
    ///   2. Stripped of its extension (bare path)
    ///   3. .ts / .tsx / .js variants
    ///   4. index.ts / index.tsx / index.js barrel files
    /// </summary>
    private static RaggableNode? ResolveInIndex(
        string resolved,
        Dictionary<string, RaggableNode> index)
    {
        // 1. Exact match (covers most .ts cases where adapter fallback already appended .ts)
        if (index.TryGetValue(resolved, out var exact)) return exact;

        // 2. Strip existing extension and probe variants (handles wrong-extension fallback)
        var bare = resolved;
        var lastDot = resolved.LastIndexOf('.');
        var lastSlash = resolved.LastIndexOf('/');
        if (lastDot > lastSlash) bare = resolved[..lastDot];

        // 3. Standard TypeScript extension variants
        foreach (var ext in new[] { ".ts", ".tsx", ".js" })
        {
            if (index.TryGetValue(bare + ext, out var byExt)) return byExt;
        }

        // 4. Barrel (directory/index.*) variants — resolves "import from './components'"
        foreach (var indexFile in new[] { "/index.ts", "/index.tsx", "/index.js" })
        {
            if (index.TryGetValue(bare + indexFile, out var barrel)) return barrel;
        }

        return null;
    }

    private static RaggableNode CreateExternalNode(string fqn)
    {
        var name = fqn.Split("::").Last();
        return new RaggableNode
        {
            Id = fqn,
            Kind = UniversalNodeKind.Module,
            Name = name,
            VirtualFilePath = string.Empty,
            Range = new NodeRange(0, 0, 0, 0, 0),
            Level = NodeLevel.L2_Module,
            Language = "external",
            SourceSnippet = string.Empty,
            Sha256 = string.Empty,
            Fqn = fqn,
        };
    }

    private static string ComputeSha(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed record FileContext(string Source, IReadOnlyList<RaggableNode> Nodes);
}
