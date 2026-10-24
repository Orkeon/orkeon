using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

public sealed record AffectedTestsResult
{
    public bool All { get; init; }
    public ImmutableList<string> TestProjects { get; init; } = [];
    public ImmutableList<string> TestClasses { get; init; } = [];
    public ImmutableList<string> Unresolved { get; init; } = [];
}

/// <summary>
/// The test projects and test classes a set of changed files reaches through the graph, or "all" when a
/// changed file cannot be attributed (mirrors the affected-tests.py script).
/// Limits, stated and not worked around: <c>project_depends</c> edges come from csproj files only; TypeScript
/// tests carry <c>Tests</c> edges from their test-tagged module.
/// The test projects are the closure over <c>project_depends</c>, so a changed file whose symbols reach no
/// <c>Tests</c> edge still reaches the test projects of its owner by the path rule.
/// </summary>
public static partial class AffectedTestsQuery
{
    private const int PageSize = 1000;
    private const int MaxDepth = 64;
    private const int MaxNodes = 100_000;

    public static async Task<AffectedTestsResult> RunAsync(
        IRaggableStore store, IReadOnlyList<string> changedFiles, string rootVirtualPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(changedFiles);
        ArgumentException.ThrowIfNullOrEmpty(rootVirtualPath);
        var root = rootVirtualPath.TrimEnd('/');
        var files = changedFiles.Select(f => f.Replace('\\', '/')).Where(f => !IsSkipped(f)).Distinct(StringComparer.Ordinal).ToList();
        var relevant = files.Where(f => !NeutralPattern().IsMatch(f)).ToList();
        if (relevant.Count == 0) return new AffectedTestsResult();

        var projects = await ProjectsAsync(store, ct).ConfigureAwait(false);
        var owners = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (var file in relevant)
        {
            var owner = OwnerOf($"{root}/{file}", projects);
            if (owner is null) unresolved.Add(file);
            else owners[file] = owner;
        }

        if (unresolved.Count > 0) return Everything(unresolved);

        var ownerIds = owners.Values.Select(o => o.Id).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var modules = await store.ExpandAsync(Expansion(ownerIds, EdgeKind.Contains, Direction.Forward, 1), ct).ConfigureAwait(false);
        var modulesByPath = modules.Nodes.Where(n => n.Level == NodeLevel.L2_Module)
            .GroupBy(n => n.VirtualFilePath, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        unresolved = [.. relevant.Where(f => !modulesByPath.ContainsKey($"{root}/{f}"))];
        if (modules.Truncated || unresolved.Count > 0) return Everything(unresolved);

        var dependents = await store.ExpandAsync(Expansion(ownerIds, EdgeKind.ProjectDepends, Direction.Backward, MaxDepth), ct).ConfigureAwait(false);
        if (dependents.Truncated) return Everything([]);
        var testProjects = dependents.Nodes
            .Where(n => n.Kind == UniversalNodeKind.Package && n.Tags.ContainsKey("marker"))
            .Select(n => RelativeDirectory(n.VirtualFilePath, root))
            .Where(IsTestPath)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableList();

        var symbols = await DescendantsAsync(store, relevant.Select(f => modulesByPath[$"{root}/{f}"]), ct).ConfigureAwait(false);
        var reached = await store.ExpandAsync(
            Expansion([.. symbols.Select(s => s.Id)], EdgeKind.Calls | EdgeKind.Tests, Direction.Backward, 2), ct).ConfigureAwait(false);
        if (reached.Truncated) return Everything([]);

        var testMethods = reached.Edges.Where(e => e.Kind == EdgeKind.Tests).Select(e => e.FromId).Distinct(StringComparer.Ordinal).ToList();
        var methodNodes = reached.Nodes.Where(n => testMethods.Contains(n.Id, StringComparer.Ordinal)).ToList();
        var testModules = methodNodes.Where(n => n.Level == NodeLevel.L2_Module).Select(n => n.Fqn);
        var parents = await store.GetManyAsync(methodNodes.Select(n => n.ParentId).OfType<string>().Distinct(StringComparer.Ordinal), ct).ConfigureAwait(false);
        return new AffectedTestsResult
        {
            TestProjects = testProjects,
            TestClasses = [.. parents.Where(p => p.Kind == UniversalNodeKind.Class).Select(p => p.Fqn).Concat(testModules).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        };
    }

    private static AffectedTestsResult Everything(List<string> unresolved)
        => new() { All = true, Unresolved = [.. unresolved.Order(StringComparer.Ordinal)] };

    private static ExpandQuery Expansion(ImmutableArray<string> seeds, EdgeKind kinds, Direction direction, int depth) => new()
    {
        Seeds = seeds,
        EdgeKinds = [kinds],
        Direction = direction,
        MaxDepth = depth,
        MaxNodes = MaxNodes,
    };

    // One read; a page that comes back full may hide projects, which the caller treats as "all".
    private static async Task<IReadOnlyList<RaggableNode>> ProjectsAsync(IRaggableStore store, CancellationToken ct)
    {
        var page = await store.QueryAsync(
            new NodeQuery { Kinds = [UniversalNodeKind.Package], Take = PageSize }, ct).ConfigureAwait(false);
        return page.Count >= PageSize ? [] : [.. page.Where(p => p.Tags.TryGetValue("marker", out var m) && m == "csproj")];
    }

    private static RaggableNode? OwnerOf(string virtualPath, IReadOnlyList<RaggableNode> projects)
        => projects
            .Where(p => virtualPath.StartsWith(p.VirtualFilePath.TrimEnd('/') + "/", StringComparison.Ordinal))
            .MaxBy(p => p.VirtualFilePath.Length);

    // The symbols below each module: types, their members, nested members - three batched reads whatever the
    // number of files. A symbol nested deeper is not seen (stated limit).
    private static async Task<List<RaggableNode>> DescendantsAsync(
        IRaggableStore store, IEnumerable<RaggableNode> modules, CancellationToken ct)
    {
        var types = await store.GetManyAsync(ChildIds(modules), ct).ConfigureAwait(false);
        var members = await store.GetManyAsync(ChildIds(types), ct).ConfigureAwait(false);
        var nested = await store.GetManyAsync(ChildIds(members), ct).ConfigureAwait(false);
        return [.. types, .. members, .. nested];
    }

    private static List<string> ChildIds(IEnumerable<RaggableNode> parents)
        => [.. parents.SelectMany(p => p.ChildrenIds).Distinct(StringComparer.Ordinal)];

    private static bool IsTestPath(string path) => path.Contains("test", StringComparison.OrdinalIgnoreCase);

    private static string RelativeDirectory(string virtualPath, string root)
    {
        var relative = virtualPath.StartsWith(root + "/", StringComparison.Ordinal) ? virtualPath[(root.Length + 1)..] : virtualPath;
        return relative.Length == 0 ? "." : relative;
    }

    private static bool IsSkipped(string file) => file.StartsWith(".claude/", StringComparison.Ordinal);

    [GeneratedRegex(@"(\.md|\.txt|\.png|\.jpg|\.svg|LICENSE[^/]*|\.gitignore|\.gitattributes|\.editorconfig)$", RegexOptions.IgnoreCase)]
    private static partial Regex NeutralPattern();
}
