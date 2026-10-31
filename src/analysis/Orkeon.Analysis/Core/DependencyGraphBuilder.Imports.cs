using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core;

/// <summary>
/// The import edges of the languages whose import names a namespace, a package path or a module path
/// rather than a file: a C# <c>using</c>, a Go <c>import</c>, a Rust <c>use</c>.
/// </summary>
public sealed partial class DependencyGraphBuilder
{
    /// <summary>The tag of a Go package node holding the module path its <c>go.mod</c> declares.</summary>
    internal const string GoModuleTag = "go_module";

    private readonly List<(string Dir, string ModulePath)> _goModules = [];
    private readonly List<string> _cargoRoots = [];

    /// <summary>
    /// Registers the package nodes of the tree: a Go package tagged with its module path
    /// (<see cref="GoModuleFile"/>) resolves the imports below that path, a Cargo package roots the
    /// <c>crate::</c> paths of the files under its <c>src/</c>.
    /// </summary>
    internal void RegisterPackages(IEnumerable<RaggableNode> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        foreach (var package in packages)
        {
            if (package.Level != NodeLevel.L1_Package) continue;
            var dir = package.VirtualFilePath.Replace('\\', '/').TrimEnd('/');
            if (package.Tags.TryGetValue(GoModuleTag, out var modulePath) && !string.IsNullOrWhiteSpace(modulePath)
                && !_goModules.Contains((dir, modulePath)))
            {
                _goModules.Add((dir, modulePath));
            }
            if (package.Tags.TryGetValue("marker", out var marker) && marker == "cargo" && !_cargoRoots.Contains(dir))
            {
                _cargoRoots.Add(dir);
            }
        }
    }

    // The whole-graph lookups of one resolution, built on the first file that needs them.
    private sealed class ImportScope(Dictionary<string, RaggableNode> index)
    {
        public Dictionary<string, RaggableNode> Index { get; } = index;
        public CSharpNamespaceIndex? CSharp { get; set; }
        public Dictionary<string, List<RaggableNode>>? GoFilesByDir { get; set; }
    }

    private static void AddImport(EdgeSet edges, RaggableNode moduleNode, RaggableNode? target)
    {
        if (target is null || moduleNode.Id == target.Id) return;
        edges.TryAdd($"{moduleNode.Id}→{target.Id}:{EdgeKind.Imports}", moduleNode.Id, target.Id, EdgeKind.Imports);
    }

    private static RaggableNode External(string id, Dictionary<string, RaggableNode> index)
        => ResolveImportTarget(id, index)!;

    // ---- C# ------------------------------------------------------------------------------------

    // A `using N;` names a namespace: the edge runs to the files declaring a type of N that the importing
    // file names, not to every file of N. A `using static N.T;` or `using A = N.T;` names the type itself.
    private void CollectCSharpUsings(
        EdgeSet edges, ImportScope scope, RaggableNode moduleNode, FileContext ctx, List<TsNode> usings)
    {
        HashSet<string>? identifiers = null;
        foreach (var directive in usings)
        {
            var name = CSharpUsingName(directive.Text ?? string.Empty);
            if (name.Length == 0) continue;

            scope.CSharp ??= BuildCSharpNamespaceIndex();
            if (scope.CSharp.Namespaces.Contains(name))
            {
                if (!scope.CSharp.Types.TryGetValue(name, out var types)) continue;
                identifiers ??= CSharpIdentifiers(ctx.Source);
                foreach (var (typeName, modules) in types)
                {
                    if (!identifiers.Contains(typeName)) continue;
                    foreach (var module in modules) AddImport(edges, moduleNode, module);
                }
                continue;
            }

            var lastDot = name.LastIndexOf('.');
            if (lastDot > 0
                && scope.CSharp.Types.TryGetValue(name[..lastDot], out var parentTypes)
                && parentTypes.TryGetValue(name[(lastDot + 1)..], out var typeModules))
            {
                foreach (var module in typeModules) AddImport(edges, moduleNode, module);
                continue;
            }

            AddImport(edges, moduleNode, External($"ext::dotnet::{name}", scope.Index));
        }
    }

    // `global using static A.B;`, `using X = A.B.C<int>;` -> the namespace or type named, without alias,
    // generic arguments, `global::` or whitespace.
    internal static string CSharpUsingName(string directive)
    {
        var text = directive.Trim().TrimEnd(';');
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        if (equals >= 0)
        {
            text = text[(equals + 1)..];
        }
        else
        {
            foreach (var keyword in new[] { "global", "using", "static", "unsafe" })
            {
                text = text.TrimStart();
                if (text.StartsWith(keyword, StringComparison.Ordinal)
                    && text.Length > keyword.Length && char.IsWhiteSpace(text[keyword.Length]))
                {
                    text = text[keyword.Length..];
                }
            }
        }
        var generic = text.IndexOf('<', StringComparison.Ordinal);
        if (generic >= 0) text = text[..generic];
        text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (text.StartsWith("global::", StringComparison.Ordinal)) text = text["global::".Length..];
        return text;
    }

    private sealed class CSharpNamespaceIndex
    {
        public HashSet<string> Namespaces { get; } = new(StringComparer.Ordinal);

        // namespace -> type short name -> the modules declaring it, in registration order.
        public Dictionary<string, Dictionary<string, List<RaggableNode>>> Types { get; } = new(StringComparer.Ordinal);
    }

    private CSharpNamespaceIndex BuildCSharpNamespaceIndex()
    {
        var result = new CSharpNamespaceIndex();
        foreach (var ctx in _files.Values)
        {
            var module = ctx.Nodes.FirstOrDefault(n => n.Level == NodeLevel.L2_Module);
            if (module is null || module.Language != "csharp") continue;

            var byId = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
            foreach (var node in ctx.Nodes) byId.TryAdd(node.Id, node);
            var namespaceNames = new Dictionary<string, string>(StringComparer.Ordinal);

            // A file-scoped `namespace N;` encloses nothing in the syntax tree: it is the top-level namespace
            // no node is a child of, and it holds the types no block namespace encloses.
            var parentIds = new HashSet<string>(ctx.Nodes.Select(n => n.ParentId ?? string.Empty), StringComparer.Ordinal);
            var fileScoped = ctx.Nodes.FirstOrDefault(n => n.Kind == UniversalNodeKind.Namespace
                && n.ParentId == module.Id && !parentIds.Contains(n.Id));

            string? NamespaceOf(RaggableNode node)
            {
                var parentId = node.ParentId;
                while (parentId is not null && byId.TryGetValue(parentId, out var parent))
                {
                    if (parent.Kind == UniversalNodeKind.Namespace) return FullName(parent);
                    parentId = parent.ParentId;
                }
                return node.Kind == UniversalNodeKind.Namespace || fileScoped is null ? null : FullName(fileScoped);
            }

            string FullName(RaggableNode ns)
            {
                if (namespaceNames.TryGetValue(ns.Id, out var known)) return known;
                var outer = NamespaceOf(ns);
                var full = outer is null ? ns.Name : $"{outer}.{ns.Name}";
                namespaceNames[ns.Id] = full;
                return full;
            }

            foreach (var node in ctx.Nodes)
            {
                if (node.Level != NodeLevel.L3_Symbol) continue;
                if (node.Kind == UniversalNodeKind.Namespace)
                {
                    result.Namespaces.Add(FullName(node));
                    continue;
                }
                if (node.Kind is not (UniversalNodeKind.Class or UniversalNodeKind.Struct or UniversalNodeKind.Record
                    or UniversalNodeKind.Interface or UniversalNodeKind.Enum or UniversalNodeKind.Type)) continue;
                var ns = NamespaceOf(node);
                if (ns is null || string.IsNullOrEmpty(node.Name)) continue;

                if (!result.Types.TryGetValue(ns, out var types))
                {
                    types = new Dictionary<string, List<RaggableNode>>(StringComparer.Ordinal);
                    result.Types[ns] = types;
                }
                if (!types.TryGetValue(node.Name, out var modules))
                {
                    modules = [];
                    types[node.Name] = modules;
                }
                if (!modules.Contains(module)) modules.Add(module);
            }
        }
        return result;
    }

    // The identifiers of a C# source, comments left out. String contents count: an interpolated string
    // holds code, and a type named in a plain string only adds an edge to a file the importer mentions.
    internal static HashSet<string> CSharpIdentifiers(string source)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i);
                i = end < 0 ? source.Length : end + 1;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
            }
            else if (c == '"')
            {
                // A `//` or `/*` inside a string literal (`"http://..."`) opens no comment: the literal is
                // scanned to its closing quote and its identifiers kept.
                var verbatim = i > 0 && source[i - 1] == '@';
                var end = StringLiteralEnd(source, i + 1, verbatim);
                AddIdentifiers(source, i + 1, end, result);
                i = Math.Min(end + 1, source.Length);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                result.Add(source[start..i]);
            }
            else
            {
                i++;
            }
        }
        return result;
    }

    // The index of the quote closing a literal whose contents start at `from`: `\"` escapes a quote in a
    // regular literal, `""` in a verbatim one. An unterminated literal ends with the source.
    private static int StringLiteralEnd(string source, int from, bool verbatim)
    {
        var i = from;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '"')
            {
                if (verbatim && i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                return i;
            }
            if (c == '\\' && !verbatim) { i += 2; continue; }
            if (c == '\n' && !verbatim) return i;
            i++;
        }
        return source.Length;
    }

    private static void AddIdentifiers(string source, int from, int to, HashSet<string> result)
    {
        var i = from;
        while (i < to)
        {
            if (char.IsLetter(source[i]) || source[i] == '_')
            {
                var start = i;
                while (i < to && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                result.Add(source[start..i]);
            }
            else
            {
                i++;
            }
        }
    }

    // ---- Go ------------------------------------------------------------------------------------

    // An import path under a module path of a go.mod maps to the directory below that module: the edges
    // run to the non-test `.go` files of that directory. Any other path is an external package.
    private void CollectGoImports(EdgeSet edges, ImportScope scope, RaggableNode moduleNode, List<TsNode> imports)
    {
        foreach (var import in imports)
        {
            foreach (var literal in Descend(import))
            {
                if (literal.Type is not ("interpreted_string_literal" or "raw_string_literal")) continue;
                var path = (literal.Text ?? string.Empty).Trim('"', '`');
                if (path.Length == 0) continue;

                var dir = GoImportDirectory(path);
                if (dir is null)
                {
                    AddImport(edges, moduleNode, External($"ext::go::{path}", scope.Index));
                    continue;
                }
                scope.GoFilesByDir ??= BuildGoFilesByDir();
                if (!scope.GoFilesByDir.TryGetValue(dir, out var files)) continue;
                foreach (var file in files) AddImport(edges, moduleNode, file);
            }
        }
    }

    private string? GoImportDirectory(string importPath)
    {
        (string Dir, string ModulePath)? best = null;
        foreach (var module in _goModules)
        {
            var matches = importPath == module.ModulePath
                || importPath.StartsWith(module.ModulePath + "/", StringComparison.Ordinal);
            if (matches && (best is null || module.ModulePath.Length > best.Value.ModulePath.Length)) best = module;
        }
        if (best is null) return null;
        var rest = importPath[best.Value.ModulePath.Length..].TrimStart('/');
        return rest.Length == 0 ? best.Value.Dir : $"{best.Value.Dir}/{rest}";
    }

    private Dictionary<string, List<RaggableNode>> BuildGoFilesByDir()
    {
        var result = new Dictionary<string, List<RaggableNode>>(StringComparer.Ordinal);
        foreach (var (filePath, ctx) in _files)
        {
            var path = filePath.Replace('\\', '/');
            if (!path.EndsWith(".go", StringComparison.Ordinal) || path.EndsWith("_test.go", StringComparison.Ordinal)) continue;
            var module = ctx.Nodes.FirstOrDefault(n => n.Level == NodeLevel.L2_Module);
            if (module is null) continue;
            var slash = path.LastIndexOf('/');
            var dir = slash > 0 ? path[..slash] : string.Empty;
            if (!result.TryGetValue(dir, out var files))
            {
                files = [];
                result[dir] = files;
            }
            files.Add(module);
        }
        return result;
    }

    // ---- Rust ----------------------------------------------------------------------------------

    // `crate::a::b::C` -> src/a/b.rs or src/a/b/mod.rs, the longest prefix that is a module file (the tail
    // may name an item); `super::` and `self::` start from the current module, a first segment naming a
    // child module of the current one too. Any other first segment (`std`, a crate) is external.
    private static void CollectRustUses(
        EdgeSet edges, ImportScope scope, string filePath, RaggableNode moduleNode, List<TsNode> uses, IReadOnlyList<string> cargoRoots)
    {
        var fileCrate = RustCrateOf(filePath.Replace('\\', '/'), cargoRoots);
        foreach (var use in uses)
        {
            var argument = use.GetChildForField("argument")?.Text;
            if (string.IsNullOrWhiteSpace(argument)) continue;
            var inline = InlineRustModules(use);
            if (inline is null) continue;
            var crate = fileCrate with { Current = [.. fileCrate.Current, .. inline] };
            foreach (var path in ExpandRustUse(argument))
            {
                AddImport(edges, moduleNode, ResolveRustPath(path, crate, scope.Index));
            }
        }
    }

    // The names of the inline `mod x { ... }` blocks enclosing a node, outermost first; null when one is unnamed.
    private static List<string>? InlineRustModules(TsNode node)
    {
        var names = new List<string>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Type != "mod_item") continue;
            var name = parent.GetChildForField("name")?.Text;
            if (string.IsNullOrEmpty(name)) return null;
            names.Insert(0, name);
        }
        return names;
    }

    private sealed record RustCrate(string RootDir, IReadOnlyList<string> CrateRootFiles, IReadOnlyList<string> Current);

    private static RustCrate RustCrateOf(string path, IReadOnlyList<string> cargoRoots)
    {
        string? srcDir = null;
        foreach (var root in cargoRoots)
        {
            var candidate = root + "/src";
            if (path.StartsWith(candidate + "/", StringComparison.Ordinal) && (srcDir is null || candidate.Length > srcDir.Length))
                srcDir = candidate;
        }
        if (srcDir is null)
        {
            var marker = path.LastIndexOf("/src/", StringComparison.Ordinal);
            if (marker >= 0) srcDir = path[..(marker + "/src".Length)];
        }

        if (srcDir is null)
        {
            // A file outside a crate's src/ (tests/, examples/, build.rs) is its own crate root.
            var slash = path.LastIndexOf('/');
            return new RustCrate(slash > 0 ? path[..slash] : string.Empty, [path], []);
        }

        var relative = path[(srcDir.Length + 1)..];
        if (relative.EndsWith(".rs", StringComparison.Ordinal)) relative = relative[..^3];
        var segments = relative.Split('/').ToList();
        if (segments[^1] == "mod") segments.RemoveAt(segments.Count - 1);
        if (segments is ["lib"] or ["main"]) segments.Clear();
        return new RustCrate(srcDir, [$"{srcDir}/lib.rs", $"{srcDir}/main.rs"], segments);
    }

    private static RaggableNode? ResolveRustPath(string path, RustCrate crate, Dictionary<string, RaggableNode> index)
    {
        var segments = path.TrimStart(':').Split("::", StringSplitOptions.RemoveEmptyEntries).ToList();
        while (segments.Count > 0 && segments[^1] is "*" or "self") segments.RemoveAt(segments.Count - 1);
        if (segments.Count == 0) return null;

        List<string> start;
        var rest = segments;
        switch (segments[0])
        {
            case "crate":
                start = [];
                rest = segments[1..];
                break;
            case "self":
                start = [.. crate.Current];
                rest = segments[1..];
                break;
            case "super":
                var supers = segments.TakeWhile(s => s == "super").Count();
                if (supers > crate.Current.Count) return null;
                start = [.. crate.Current.Take(crate.Current.Count - supers)];
                rest = segments[supers..];
                break;
            default:
                start = [.. crate.Current];
                if (RustModuleFile([.. start, segments[0]], crate, index) is null)
                    return External($"ext::cargo::{segments[0]}", index);
                break;
        }

        for (var k = rest.Count; k >= 0; k--)
        {
            var file = RustModuleFile([.. start, .. rest.Take(k)], crate, index);
            if (file is not null) return file;
        }
        return null;
    }

    private static RaggableNode? RustModuleFile(List<string> segments, RustCrate crate, Dictionary<string, RaggableNode> index)
    {
        if (segments.Count == 0)
        {
            foreach (var rootFile in crate.CrateRootFiles)
            {
                if (index.TryGetValue(rootFile, out var root)) return root;
            }
            return null;
        }
        var stem = $"{crate.RootDir}/{string.Join('/', segments)}";
        if (index.TryGetValue(stem + ".rs", out var file) && file.Level == NodeLevel.L2_Module) return file;
        if (index.TryGetValue(stem + "/mod.rs", out var mod) && mod.Level == NodeLevel.L2_Module) return mod;
        return null;
    }

    // `crate::a::{b::C, d::{self, E as F}}` -> crate::a::b::C, crate::a::d::self, crate::a::d::E.
    internal static List<string> ExpandRustUse(string argument)
    {
        var words = argument.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        // Drop the `as alias` pairs, then the whitespace.
        var kept = new List<string>(words.Length);
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i] == "as" && i + 1 < words.Length)
            {
                var alias = words[++i];
                // The alias may carry the list punctuation that follows it (`B as C,` or `B as C}`).
                var tail = alias.SkipWhile(c => char.IsLetterOrDigit(c) || c == '_');
                kept.Add(string.Concat(tail));
                continue;
            }
            kept.Add(words[i]);
        }
        var result = new List<string>();
        Expand(string.Concat(kept), string.Empty, result);
        return result;
    }

    private static void Expand(string text, string prefix, List<string> result)
    {
        var open = text.IndexOf('{', StringComparison.Ordinal);
        if (open < 0)
        {
            if (text.Length > 0) result.Add(prefix + text);
            return;
        }
        var close = text.LastIndexOf('}');
        if (close < open) return;
        var head = prefix + text[..open];
        var inner = text[(open + 1)..close];
        var depth = 0;
        var itemStart = 0;
        for (var i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || (inner[i] == ',' && depth == 0))
            {
                var item = inner[itemStart..i];
                if (item.Length > 0) Expand(item, head, result);
                itemStart = i + 1;
            }
            else if (inner[i] == '{') depth++;
            else if (inner[i] == '}') depth--;
        }
    }
}
