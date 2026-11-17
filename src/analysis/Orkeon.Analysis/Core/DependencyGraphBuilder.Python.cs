using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core;

/// <summary>
/// The import edges of Python (#406). An import names a module by a dotted path, not a file: it is
/// read from the syntax node (<c>dotted_name</c>, <c>relative_import</c>) and resolved against the
/// files of the index before it is called external.
/// </summary>
public sealed partial class DependencyGraphBuilder
{
    /// <summary>The first segment of an import that names no module: a compiler directive.</summary>
    private const string PythonFuture = "__future__";

    private static void CollectPythonImports(
        EdgeSet edges, ImportScope scope, string filePath, RaggableNode moduleNode, List<TsNode> imports)
    {
        var path = filePath.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        var dir = slash > 0 ? path[..slash] : string.Empty;
        foreach (var import in imports)
        {
            if (import.Type == "import_from_statement")
                AddPythonFromImport(edges, scope, moduleNode, dir, import);
            else
                foreach (var module in PythonImportedNames(import.NamedChildren))
                    AddPythonAbsoluteImport(edges, scope, moduleNode, dir, module, []);
        }
    }

    /// <summary><c>from X import a, b</c>: each name that is a submodule of X reaches its file, the others reach X.</summary>
    private static void AddPythonFromImport(
        EdgeSet edges, ImportScope scope, RaggableNode moduleNode, string dir, TsNode import)
    {
        // The first named child is the module (X, or the dots of a relative import); what follows it
        // is the imported names. A wildcard names none, so it reaches X alone.
        var named = import.NamedChildren.ToList();
        var at = named.FindIndex(c => c.Type is "dotted_name" or "relative_import");
        if (at < 0) return;
        var source = named[at];
        var from = source.Text?.Trim();
        if (string.IsNullOrEmpty(from)) return;
        var names = PythonImportedNames(named.Skip(at + 1));

        if (source.Type != "relative_import")
        {
            AddPythonAbsoluteImport(edges, scope, moduleNode, dir, from, names);
            return;
        }

        // A leading dot is the importing file's package, each further dot one package up.
        var dots = from.TakeWhile(c => c == '.').Count();
        var package = dir;
        for (var i = 1; i < dots; i++)
        {
            var up = package.LastIndexOf('/');
            if (up <= 0) return;
            package = package[..up];
        }
        var module = from[dots..].Trim();
        // A relative import never leaves the project: one that reaches no indexed file draws nothing.
        foreach (var target in PythonTargets(package, module, names, scope.Index))
            AddImport(edges, moduleNode, target);
    }

    /// <summary>
    /// An absolute import, resolved as Python resolves it for a file run from its own folder or from
    /// a folder above it: the importing file's folder first, then each folder above, nearest first.
    /// The first folder under which the module is a file of the index wins; when none holds it, the
    /// import is external (<c>ext::pypi::&lt;first segment&gt;</c>).
    /// </summary>
    private static void AddPythonAbsoluteImport(
        EdgeSet edges, ImportScope scope, RaggableNode moduleNode, string dir, string module, IReadOnlyList<string> names)
    {
        var first = module.Split('.')[0];
        if (first.Length == 0 || first == PythonFuture) return;

        for (var root = dir; ; root = root[..root.LastIndexOf('/')])
        {
            var targets = PythonTargets(root, module, names, scope.Index);
            if (targets.Count > 0)
            {
                foreach (var target in targets) AddImport(edges, moduleNode, target);
                return;
            }
            if (root.LastIndexOf('/') <= 0) break;
        }

        AddImport(edges, moduleNode, External($"ext::pypi::{first}", scope.Index));
    }

    /// <summary>
    /// The files <c>from module import names</c> reaches under <paramref name="root"/>: the file of
    /// each name that is a submodule, and the module's own file for the names that are not (or for
    /// a plain <c>import module</c>, a wildcard). Empty when nothing of it is in the index.
    /// </summary>
    private static List<RaggableNode> PythonTargets(
        string root, string module, IReadOnlyList<string> names, Dictionary<string, RaggableNode> index)
    {
        var targets = new List<RaggableNode>();
        var basePath = module.Length == 0 ? root : $"{root}/{module.Replace('.', '/')}";
        var needsModule = names.Count == 0;
        foreach (var name in names)
        {
            if (PythonModuleFile($"{basePath}/{name.Replace('.', '/')}", index) is { } submodule)
                targets.Add(submodule);
            else
                needsModule = true;
        }

        if (needsModule)
        {
            // `from . import x` with no module: the names that are no submodule come from the package itself.
            var own = module.Length == 0
                ? (index.TryGetValue($"{basePath}/__init__.py", out var init) ? init : null)
                : PythonModuleFile(basePath, index);
            if (own is not null) targets.Add(own);
        }

        return targets;
    }

    /// <summary>The file of a module path without its extension: <c>path.py</c>, or the package's <c>path/__init__.py</c>.</summary>
    private static RaggableNode? PythonModuleFile(string path, Dictionary<string, RaggableNode> index)
    {
        if (index.TryGetValue(path + ".py", out var file) && file.Level == NodeLevel.L2_Module) return file;
        if (index.TryGetValue(path + "/__init__.py", out var package) && package.Level == NodeLevel.L2_Module) return package;
        return null;
    }

    /// <summary>The dotted names a list of import children names: <c>a.b</c>, or the <c>a.b</c> of <c>a.b as c</c>.</summary>
    private static List<string> PythonImportedNames(IEnumerable<TsNode> children)
    {
        var names = new List<string>();
        foreach (var child in children)
        {
            var name = child.Type switch
            {
                "dotted_name" => child.Text,
                "aliased_import" => child.GetChildForField("name")?.Text,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
        }
        return names;
    }
}
