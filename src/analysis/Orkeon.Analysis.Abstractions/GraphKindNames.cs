using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions;

/// <summary>
/// The lowercase workflow names <c>edge_kinds</c> and <c>node_kinds</c> take in the graph tools, and their mapping
/// to <see cref="EdgeKind"/> and to graph nodes. Legacy <see cref="EdgeKind"/> member names stay accepted.
/// </summary>
public static class GraphKindNames
{
    private const string TestTag = "test";

    private static readonly ImmutableDictionary<string, EdgeKind> EdgeByName = new Dictionary<string, EdgeKind>
    {
        ["contains"] = EdgeKind.Contains,
        ["calls"] = EdgeKind.Calls,
        ["imports"] = EdgeKind.Imports,
        ["inherits"] = EdgeKind.Extends | EdgeKind.Implements,
        ["tests"] = EdgeKind.Tests,
        ["documents"] = EdgeKind.Documents,
        ["project_depends"] = EdgeKind.ProjectDepends,
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly ImmutableDictionary<string, ImmutableArray<UniversalNodeKind>> KindsByName = new Dictionary<string, ImmutableArray<UniversalNodeKind>>
    {
        ["file"] = [UniversalNodeKind.Module],
        ["project"] = [UniversalNodeKind.Package],
        ["namespace"] = [UniversalNodeKind.Namespace],
        ["type"] =
        [
            UniversalNodeKind.Class, UniversalNodeKind.Interface, UniversalNodeKind.Trait, UniversalNodeKind.Struct,
            UniversalNodeKind.Enum, UniversalNodeKind.Record, UniversalNodeKind.Type,
        ],
        ["member"] =
        [
            UniversalNodeKind.Function, UniversalNodeKind.Method, UniversalNodeKind.Constructor, UniversalNodeKind.Destructor,
            UniversalNodeKind.Operator, UniversalNodeKind.Property, UniversalNodeKind.Field, UniversalNodeKind.Constant,
            UniversalNodeKind.Variable,
        ],
        ["doc_section"] = [UniversalNodeKind.Section],
        ["test"] = [UniversalNodeKind.Method],
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The workflow edge names, in documentation order.</summary>
    public static ImmutableArray<string> EdgeKindNames { get; } =
        ["contains", "calls", "imports", "inherits", "tests", "documents", "project_depends"];

    /// <summary>The workflow node names, in documentation order.</summary>
    public static ImmutableArray<string> NodeKindNames { get; } =
        ["file", "project", "namespace", "type", "member", "doc_section", "test"];

    /// <summary>Parses a workflow name or a legacy <see cref="EdgeKind"/> member name, case-insensitively.</summary>
    public static bool TryParseEdgeKind(string? name, out EdgeKind kind)
    {
        kind = EdgeKind.None;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (EdgeByName.TryGetValue(name, out kind)) return true;
        var legacy = Enum.GetNames<EdgeKind>().FirstOrDefault(n => n != nameof(EdgeKind.None) && string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        return legacy is not null && Enum.TryParse(legacy, out kind);
    }

    /// <summary>Parses a workflow node name into the predicate a node must satisfy.</summary>
    public static bool TryParseNodeKind(string? name, out Func<RaggableNode, bool> matches)
    {
        matches = static _ => false;
        if (string.IsNullOrWhiteSpace(name) || !KindsByName.TryGetValue(name, out var kinds)) return false;
        var tagged = IsTestName(name);
        matches = n => kinds.Contains(n.EffectiveKind) && (!tagged || n.Tags.ContainsKey(TestTag));
        return true;
    }

    /// <summary>Parses every edge name; an unknown name throws an <see cref="ArgumentException"/> that names it.</summary>
    public static ImmutableArray<EdgeKind> ParseEdgeKinds(ImmutableArray<string> names)
    {
        if (names.IsDefaultOrEmpty) return [];
        var kinds = ImmutableArray.CreateBuilder<EdgeKind>(names.Length);
        foreach (var name in names)
        {
            if (!TryParseEdgeKind(name, out var kind))
                throw new ArgumentException($"Unknown edge kind '{name}'. Known edge kinds: {string.Join(", ", EdgeKindNames)}.", nameof(names));
            kinds.Add(kind);
        }

        return kinds.MoveToImmutable();
    }

    /// <summary>Parses every node name into one predicate (any match); an unknown name throws an <see cref="ArgumentException"/> that names it.</summary>
    public static Func<RaggableNode, bool> ParseNodeKinds(ImmutableArray<string> names)
    {
        var predicates = new List<Func<RaggableNode, bool>>();
        if (!names.IsDefaultOrEmpty)
        {
            foreach (var name in names)
            {
                if (!TryParseNodeKind(name, out var matches))
                    throw new ArgumentException($"Unknown node kind '{name}'. Known node kinds: {string.Join(", ", NodeKindNames)}.", nameof(names));
                predicates.Add(matches);
            }
        }

        return node => predicates.Exists(p => p(node));
    }

    /// <summary>
    /// The store queries (without level or paging) that select the nodes the names ask for: one for the plain kinds,
    /// one more for <c>test</c>, which also needs the test tag. An unknown name throws an <see cref="ArgumentException"/>.
    /// </summary>
    public static ImmutableArray<NodeQuery> ParseNodeQueries(ImmutableArray<string> names)
    {
        var plain = new HashSet<UniversalNodeKind>();
        var withTest = false;
        foreach (var name in names.IsDefaultOrEmpty ? [] : names)
        {
            if (string.IsNullOrWhiteSpace(name) || !KindsByName.TryGetValue(name, out var kinds))
                throw new ArgumentException($"Unknown node kind '{name}'. Known node kinds: {string.Join(", ", NodeKindNames)}.", nameof(names));
            if (IsTestName(name)) withTest = true;
            else plain.UnionWith(kinds);
        }

        var queries = ImmutableArray.CreateBuilder<NodeQuery>(2);
        if (plain.Count > 0) queries.Add(new NodeQuery { Kinds = [.. plain] });
        if (withTest)
        {
            queries.Add(new NodeQuery
            {
                Kinds = [UniversalNodeKind.Method],
                Tags = ImmutableDictionary<string, string>.Empty.Add(TestTag, "true"),
            });
        }

        return queries.ToImmutable();
    }

    private static bool IsTestName(string name) => string.Equals(name, TestTag, StringComparison.OrdinalIgnoreCase);
}
