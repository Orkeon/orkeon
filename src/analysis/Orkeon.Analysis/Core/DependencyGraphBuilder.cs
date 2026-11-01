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
public sealed partial class DependencyGraphBuilder
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
        => RegisterFile(filePath, source, nodes, _adapter);

    /// <summary>
    /// Registers a file parsed by <paramref name="adapter"/>, another language of the builder's graph group
    /// (<see cref="GraphGroupOf"/>): a C file and the C++-parsed header it includes resolve against one index.
    /// </summary>
    public void RegisterFile(string filePath, string source, IReadOnlyList<RaggableNode> nodes, ILanguageAdapter adapter)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(adapter);
        _files[filePath] = new FileContext(source, nodes, adapter);
    }

    /// <summary>
    /// The graph a language's edges are resolved in. Languages whose files import one another share one:
    /// C and C++ (a <c>.h</c> header is parsed as C++), JavaScript and TypeScript.
    /// </summary>
    public static string GraphGroupOf(string languageName) => languageName switch
    {
        "c" or "cpp" => "c-family",
        "javascript" or "typescript" => "ecmascript",
        _ => languageName,
    };

    public Task<IReadOnlyList<RaggableEdge>> BuildDependenciesAsync(
        IReferenceResolver resolver, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return BuildDependenciesCoreAsync(resolver, [.. _files.Keys], ct);
    }

    /// <summary>
    /// Builds the edges of <paramref name="files"/> only. The nodes of the unchanged files are
    /// registered from the store (no source, no parse) so that references resolve against the
    /// whole graph, and only the changed files are parsed.
    /// </summary>
    public Task<IReadOnlyList<RaggableEdge>> BuildForFilesAsync(
        IEnumerable<string> files,
        IEnumerable<RaggableNode> unchangedNodes,
        IReferenceResolver resolver,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(unchangedNodes);
        ArgumentNullException.ThrowIfNull(resolver);
        foreach (var group in unchangedNodes.GroupBy(n => n.VirtualFilePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!_files.ContainsKey(group.Key)) RegisterFile(group.Key, string.Empty, [.. group]);
        }
        return BuildDependenciesCoreAsync(resolver, [.. files], ct);
    }

    private async Task<IReadOnlyList<RaggableEdge>> BuildDependenciesCoreAsync(
        IReferenceResolver resolver, IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        var index = BuildGlobalIndex();
        // Built once: the imports add only external modules to the index, never a symbol.
        var byShortName = BuildShortNameIndex(index);
        var scope = new ImportScope(index);
        var edges = new EdgeSet();
        var unresolvedCalls = new List<UnresolvedRef>();
        using var queries = new QueryCache();

        foreach (var filePath in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            var ctx = _files[filePath];
            var moduleNode = ctx.Nodes.FirstOrDefault(n => n.Level == NodeLevel.L2_Module);
            if (moduleNode is null) continue;

            CollectFileEdges(edges, unresolvedCalls, scope, byShortName, queries, filePath, moduleNode, ctx);
        }

        var resolved = await resolver.ResolveAsync(unresolvedCalls, index, ct).ConfigureAwait(false);
        var all = edges.Edges;
        all.AddRange(resolved);

        ApplyInverseLinks(all, index);
        return all;
    }

    private void CollectFileEdges(
        EdgeSet edges,
        List<UnresolvedRef> unresolvedCalls,
        ImportScope scope,
        ShortNameIndex byShortName,
        QueryCache queries,
        string filePath,
        RaggableNode moduleNode,
        FileContext ctx)
    {
        using var parsed = _pool.Parse(ctx.Source, ctx.Adapter.LanguageName);
        var language = parsed.Tree.Language;

        var importsStart = edges.Edges.Count;
        CollectImports(edges, scope, filePath, moduleNode, ctx, parsed.Root, queries.Get(language, ctx.Adapter.LanguageName, ctx.Adapter.ImportQuery));
        var importedFiles = ImportedFiles(edges.Edges, importsStart, moduleNode, scope.Index);
        CollectInheritance(edges, byShortName, importedFiles, ctx, parsed.Root, queries.Get(language, ctx.Adapter.LanguageName, ctx.Adapter.InheritanceQuery));
        CollectCalls(unresolvedCalls, ctx, moduleNode, parsed.Root, queries.Get(language, ctx.Adapter.LanguageName, ctx.Adapter.CallQuery));
    }

    private void CollectImports(
        EdgeSet edges,
        ImportScope scope,
        string filePath,
        RaggableNode moduleNode,
        FileContext ctx,
        TsNode root,
        TsQuery? query)
    {
        var importNodes = RunQuery(query, root, "import");
        switch (ctx.Adapter.LanguageName)
        {
            case "csharp":
                CollectCSharpUsings(edges, scope, moduleNode, ctx, importNodes);
                return;
            case "go":
                CollectGoImports(edges, scope, moduleNode, importNodes);
                return;
            case "rust":
                CollectRustUses(edges, scope, filePath, moduleNode, importNodes, _cargoRoots);
                return;
        }

        foreach (var importNode in importNodes)
        {
            var spec = ExtractImportSpecifier(importNode);
            if (spec is null) continue;

            var resolved = ctx.Adapter.ResolveImportPath(spec, filePath);
            if (resolved is null) continue;

            AddImport(edges, moduleNode, ResolveImportTarget(resolved, scope.Index));
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

    /// <summary>
    /// The files the module's import edges reach: the edges <see cref="CollectImports"/> just added, from
    /// <paramref name="start"/> on (a relative import, a Go or Rust module, a C# <c>using</c> of a namespace
    /// reaches the files declaring the types the file names).
    /// </summary>
    private static HashSet<string> ImportedFiles(
        List<RaggableEdge> edges, int start, RaggableNode moduleNode, Dictionary<string, RaggableNode> index)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        for (var i = start; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge.Kind != EdgeKind.Imports || edge.FromId != moduleNode.Id) continue;
            if (index.TryGetValue(edge.ToId, out var target) && !string.IsNullOrEmpty(target.VirtualFilePath))
                files.Add(target.VirtualFilePath);
        }
        return files;
    }

    private static void CollectInheritance(
        EdgeSet edges,
        ShortNameIndex byShortName,
        HashSet<string> importedFiles,
        FileContext ctx,
        TsNode root,
        TsQuery? query)
    {
        foreach (var (captureName, kind) in new[] { ("extends", EdgeKind.Extends), ("implements", EdgeKind.Implements) })
        {
            var clauses = RunQuery(query, root, captureName);
            foreach (var clause in clauses)
            {
                var ownerSymbol = FindEnclosingSymbol(clause, ctx.Nodes);
                if (ownerSymbol is null) continue;
                foreach (var name in ExtractTypeNames(clause))
                {
                    var target = ResolveByShortName(name, byShortName, ownerSymbol, importedFiles);
                    if (target is null) continue;
                    if (ownerSymbol.Id == target.Id) continue;
                    var edgeKind = InheritanceKind(kind, ownerSymbol, target);
                    edges.TryAdd($"{ownerSymbol.Id}→{target.Id}:{edgeKind}", ownerSymbol.Id, target.Id, edgeKind);
                }
            }
        }
    }

    private static void CollectCalls(
        List<UnresolvedRef> unresolved,
        FileContext ctx,
        RaggableNode moduleNode,
        TsNode root,
        TsQuery? query)
    {
        var calls = RunQuery(query, root, "call");
        foreach (var call in calls)
        {
            var ownerSymbol = FindEnclosingSymbol(call, ctx.Nodes);
            var name = ExtractCallName(call);
            if (ctx.Adapter.LanguageName is "typescript" or "javascript")
            {
                ownerSymbol ??= moduleNode;
                if (ownerSymbol is not null && IsBareIdentifierCall(call) && TestEdgeLinker.IsTestCallName(name))
                    TestEdgeLinker.MarkTest(ownerSymbol);
            }

            if (ownerSymbol is null || string.IsNullOrEmpty(name)) continue;

            var location = new SourceLocation(
                ownerSymbol.VirtualFilePath,
                call.StartPosition.Row + 1,
                call.EndPosition.Row + 1,
                ComputeSha(call.Text ?? string.Empty));

            TagCall(moduleNode, name, ownerSymbol.Id);

            var kind = call.Type is "new_expression" or "object_creation_expression" ? ReferenceKind.Instantiate : ReferenceKind.Call;
            unresolved.Add(new UnresolvedRef(ownerSymbol.Fqn, name, kind, location));
        }
    }

    internal const string CallsTagPrefix = "calls:";

    // The names a file calls, with the ids of the symbols that call them, stay on its module node so an
    // incremental reindex can relink the file when a changed file defines or drops one of them.
    private static void TagCall(RaggableNode module, string name, string ownerId)
    {
        var key = CallsTagPrefix + name;
        if (!module.TagsMutable.TryGetValue(key, out var owners)) module.TagsMutable[key] = ownerId;
        else if (!owners.Split('\n').Contains(ownerId)) module.TagsMutable[key] = owners + '\n' + ownerId;
    }

    private static void ApplyInverseLinks(
        List<RaggableEdge> edges,
        Dictionary<string, RaggableNode> index)
    {
        // The index is keyed by FQN, path and id; an id it lacks (an unresolved call target)
        // is looked up by node id, the first node in index order, as a scan would find it.
        Dictionary<string, RaggableNode>? byId = null;
        RaggableNode? Find(string id)
        {
            if (index.TryGetValue(id, out var node)) return node;
            if (byId is null)
            {
                byId = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
                foreach (var candidate in index.Values) byId.TryAdd(candidate.Id, candidate);
            }
            return byId.GetValueOrDefault(id);
        }

        foreach (var edge in edges)
        {
            var fromNode = Find(edge.FromId);
            var toNode = Find(edge.ToId);

            if (fromNode is null || toNode is null) continue;

            LinkNodes(edge.Kind, fromNode, toNode);
        }
    }

    internal static void LinkNodes(EdgeKind kind, RaggableNode fromNode, RaggableNode toNode)
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

    private static ShortNameIndex BuildShortNameIndex(
        Dictionary<string, RaggableNode> index)
    {
        var result = new ShortNameIndex();
        // A node sits in the index under its FQN and its id: keep its first occurrence.
        var seen = new HashSet<RaggableNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in index.Values)
        {
            if (node.Level != NodeLevel.L3_Symbol || !seen.Add(node)) continue;
            Add(result.ByName, node.Name, node);
            if (node.EffectiveKind == UniversalNodeKind.Namespace) Add(result.NamespacesByFile, node.VirtualFilePath, node);
        }
        return result;

        static void Add(Dictionary<string, List<RaggableNode>> map, string key, RaggableNode node)
        {
            if (!map.TryGetValue(key, out var bucket))
            {
                bucket = [];
                map[key] = bucket;
            }
            bucket.Add(node);
        }
    }

    /// <summary>The L3 symbols by short name, and the namespace declarations of each file.</summary>
    private sealed class ShortNameIndex
    {
        public Dictionary<string, List<RaggableNode>> ByName { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<RaggableNode>> NamespacesByFile { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The kind of an inheritance edge. A grammar that lists every base in one clause (C#'s <c>base_list</c>)
    /// captures it as <c>extends</c>: a class, struct or record naming an interface implements it, as Java's
    /// <c>implements</c> and TypeScript's <c>implements</c> say; an interface naming an interface extends it, as
    /// both languages say too.
    /// </summary>
    internal static EdgeKind InheritanceKind(EdgeKind captured, RaggableNode owner, RaggableNode target)
        => captured == EdgeKind.Extends
            && target.EffectiveKind == UniversalNodeKind.Interface
            && owner.EffectiveKind != UniversalNodeKind.Interface
                ? EdgeKind.Implements
                : captured;

    /// <summary>
    /// The type a short name in an inheritance clause of <paramref name="owner"/> denotes, or null when it cannot
    /// be told. In order: a candidate of the owner's file; the one declared in the closest named scope enclosing
    /// the owner (its namespace, or a namespace or type containing it), a candidate with no named scope never
    /// enclosing anything; the one a file the owner's file imports declares (a relative import, a C#
    /// <c>using</c> of its namespace); the only type declaring the name. Otherwise no edge, which beats an edge to
    /// the wrong type. Declarations of one C# namespace (a partial type) are one type, the first by FQN; in a
    /// module language (TypeScript, Python, Go...) two files declaring the name declare two types.
    /// </summary>
    private static RaggableNode? ResolveByShortName(
        string name,
        ShortNameIndex index,
        RaggableNode owner,
        HashSet<string> importedFiles)
    {
        if (!index.ByName.TryGetValue(name, out var candidates) || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        var sameLanguage = candidates.Where(c => string.Equals(c.Language, owner.Language, StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = (sameLanguage.Count > 0 ? sameLanguage : candidates)
            .OrderBy(c => c.Fqn, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        var sameFile = pool.Find(c => c.VirtualFilePath == owner.VirtualFilePath);
        if (sameFile is not null) return sameFile;

        var ownerScope = ScopeOf(owner, index, includeSelf: true);
        var scoped = pool.Select(c => (Node: c, Scope: ScopeOf(c, index, includeSelf: false))).ToList();
        var enclosing = scoped
            .Where(c => c.Scope.Length > 0 && IsPrefix(c.Scope, ownerScope))
            .OrderByDescending(c => c.Scope.Length)
            .FirstOrDefault();
        if (enclosing.Node is not null) return enclosing.Node;

        var imported = scoped.Where(c => importedFiles.Contains(c.Node.VirtualFilePath)).ToList();
        if (imported.Count > 0) return OnlyType(imported);

        return OnlyType(scoped);
    }

    /// <summary>The first candidate when they all declare one type, else null.</summary>
    private static RaggableNode? OnlyType(List<(RaggableNode Node, string[] Scope)> candidates)
    {
        var types = candidates.Select(c => TypeKey(c.Node, c.Scope)).Distinct(StringComparer.Ordinal).Count();
        return types == 1 ? candidates[0].Node : null;
    }

    // C# declarations of one namespace are one type (a partial type, even across files); in a module language a
    // type belongs to its file.
    private static string TypeKey(RaggableNode node, string[] scope)
        => string.Equals(node.Language, "csharp", StringComparison.OrdinalIgnoreCase)
            ? string.Join('.', scope)
            : node.VirtualFilePath + "::" + string.Join('.', scope);

    /// <summary>
    /// The names of the scopes declaring <paramref name="node"/> (namespaces, enclosing types), outermost first:
    /// its FQN past the file path, split at <c>::</c> and <c>.</c>, without its own name unless asked. A namespace
    /// declared for the whole file (C#'s <c>namespace X;</c>) encloses no node, so it comes first when it precedes
    /// the node.
    /// </summary>
    private static string[] ScopeOf(RaggableNode node, ShortNameIndex index, bool includeSelf)
    {
        var fqn = node.Fqn;
        var filePrefix = node.VirtualFilePath + "::";
        if (!string.IsNullOrEmpty(node.VirtualFilePath) && fqn.StartsWith(filePrefix, StringComparison.Ordinal))
            fqn = fqn[filePrefix.Length..];
        var parts = fqn.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (!includeSelf && parts.Length > 0) parts = parts[..^1];

        if (node.EffectiveKind == UniversalNodeKind.Namespace
            || !index.NamespacesByFile.TryGetValue(node.VirtualFilePath, out var namespaces))
        {
            return parts;
        }

        var fileScoped = namespaces
            // A block namespace holds its members (and sits in their FQN already); a file-wide one has no block.
            .Where(n => n.ChildrenIds.Count == 0 && !n.SourceSnippet.Contains('{', StringComparison.Ordinal) && n.Range.EndByte <= node.Range.StartByte)
            .OrderByDescending(n => n.Range.StartByte)
            .FirstOrDefault();
        return fileScoped is null
            ? parts
            : [.. fileScoped.Name.Split(Separators, StringSplitOptions.RemoveEmptyEntries), .. parts];
    }

    private static readonly string[] Separators = ["::", "."];

    private static bool IsPrefix(string[] prefix, string[] of)
    {
        if (prefix.Length > of.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(prefix[i], of[i], StringComparison.Ordinal)) return false;
        }
        return true;
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
            // C names its include `string_literal` or `system_lib_string`, PHP its double-quoted string
            // `encapsed_string`; a Bash `source` path is a `word` or a `raw_string`.
            if (descendant.Type is "string" or "string_fragment" or "string_literal" or "system_lib_string"
                or "encapsed_string" or "word" or "raw_string")
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
            // `name` is PHP's identifier, `constant` Ruby's class name.
            if (descendant.Type is "identifier" or "type_identifier" or "name" or "constant")
            {
                var text = descendant.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(text)) yield return text;
            }
        }
    }

    private static bool IsBareIdentifierCall(TsNode call) =>
        (call.GetChildForField("function") ?? call.GetChildForField("callee"))?.Type == "identifier";

    private static string ExtractCallName(TsNode call)
    {
        // Java and PHP name the method `name`, Ruby `method`; a constructor call names its `type`.
        var funcChild = call.GetChildForField("function")
            ?? call.GetChildForField("constructor")
            ?? call.GetChildForField("callee")
            ?? call.GetChildForField("name")
            ?? call.GetChildForField("method")
            ?? call.GetChildForField("type");
        if (funcChild is not null)
        {
            return ShortNameOf(funcChild);
        }
        var first = call.FirstNamedChild ?? call.FirstChild;
        if (first is not null) return ShortNameOf(first);
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

    private static List<TsNode> RunQuery(TsQuery? query, TsNode root, string captureName)
    {
        if (query is null) return [];
        var results = new List<TsNode>();
        using var cursor = query.Execute(root);
        foreach (var match in cursor.Matches)
        {
            foreach (var capture in match.Captures)
            {
                if (capture.Name == captureName) results.Add(capture.Node);
            }
        }
        return results;
    }

    // The edges of one resolution, in insertion order, with their ids for the duplicate check.
    private sealed class EdgeSet
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        public List<RaggableEdge> Edges { get; } = [];

        public void TryAdd(string id, string fromId, string toId, EdgeKind kind)
        {
            if (_ids.Add(id)) Edges.Add(new RaggableEdge(id, fromId, toId, kind));
        }
    }

    // The queries of one resolution, compiled once per language instead of once per file.
    private sealed class QueryCache : IDisposable
    {
        private readonly Dictionary<(string Language, string Text), TsQuery?> _queries = [];

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort query compilation: a malformed/unsupported tree-sitter query yields no matches rather than crashing edge resolution.")]
        public TsQuery? Get(TsLanguage language, string languageName, string queryText)
        {
            if (string.IsNullOrWhiteSpace(queryText)) return null;
            if (_queries.TryGetValue((languageName, queryText), out var cached)) return cached;
            TsQuery? query;
            try { query = language.CreateQuery(queryText); }
            catch { query = null; }
            _queries[(languageName, queryText)] = query;
            return query;
        }

        public void Dispose()
        {
            foreach (var query in _queries.Values) query?.Dispose();
            _queries.Clear();
        }
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
    ///   4. index.ts / index.tsx / index.js / index.jsx barrel files
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
        foreach (var ext in new[] { ".ts", ".tsx", ".js", ".jsx" })
        {
            if (index.TryGetValue(bare + ext, out var byExt)) return byExt;
        }

        // 4. Barrel (directory/index.*) variants — resolves "import from './components'"
        foreach (var indexFile in new[] { "/index.ts", "/index.tsx", "/index.js", "/index.jsx" })
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

    private sealed record FileContext(string Source, IReadOnlyList<RaggableNode> Nodes, ILanguageAdapter Adapter);
}
