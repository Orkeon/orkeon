using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core;

public sealed record AnchorClosure
{
    public ImmutableList<string> Nodes { get; init; } = [];
    public ImmutableList<string> Unresolved { get; init; } = [];
    public bool Truncated { get; init; }
}

public sealed record AnchorsOverlap
{
    public ImmutableList<string> Shared { get; init; } = [];
    public bool Disjoint { get; init; }
    public bool Truncated { get; init; }
}

public static class AnchorsQuery
{
    private const int MaxDepth = 64;
    private const int MaxNodes = 100_000;

    // Anchors resolve in one batched read; the closure is two expansions (contains down, then callers and
    // tests at depth 1 of the anchors and what they contain), whatever the number of anchors.
    public static async Task<AnchorClosure> ClosureAsync(
        IRaggableStore store, IReadOnlyList<string> fqns, IReadOnlyList<string> files, string rootVirtualPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fqns);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(rootVirtualPath);
        var root = rootVirtualPath.TrimEnd('/');
        var anchors = fqns.Select(f => (Label: f, Key: f))
            .Concat(files.Select(f => (Label: f, Key: $"{root}/{f.Replace('\\', '/')}")))
            .DistinctBy(a => a.Key, StringComparer.Ordinal).ToList();
        var found = await store.GetManyAsync(anchors.Select(a => a.Key), ct).ConfigureAwait(false);
        var known = found.SelectMany(n => new[] { n.Id, n.Fqn }).ToHashSet(StringComparer.Ordinal);
        var unresolved = anchors.Where(a => !known.Contains(a.Key)).Select(a => a.Label)
            .Order(StringComparer.Ordinal).ToImmutableList();
        if (found.Count == 0) return new AnchorClosure { Unresolved = unresolved };

        var contained = await store.ExpandAsync(Expansion([.. found.Select(n => n.Id)], EdgeKind.Contains, Direction.Forward, MaxDepth), ct).ConfigureAwait(false);
        var members = contained.Nodes.Select(n => n.Id).Union(found.Select(n => n.Id), StringComparer.Ordinal).ToImmutableArray();
        var reached = await store.ExpandAsync(Expansion(members, EdgeKind.Calls | EdgeKind.Tests, Direction.Backward, 1), ct).ConfigureAwait(false);
        return new AnchorClosure
        {
            Nodes = [.. contained.Nodes.Concat(reached.Nodes).Select(n => n.Fqn).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            Unresolved = unresolved,
            Truncated = contained.Truncated || reached.Truncated,
        };
    }

    public static AnchorsOverlap Overlap(AnchorClosure a, AnchorClosure b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var shared = a.Nodes.Intersect(b.Nodes, StringComparer.Ordinal)
            .Concat(a.Unresolved.Intersect(b.Unresolved, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal).ToImmutableList();
        var truncated = a.Truncated || b.Truncated;
        var unresolved = a.Unresolved.Count > 0 || b.Unresolved.Count > 0;
        return new AnchorsOverlap { Shared = shared, Disjoint = shared.Count == 0 && !truncated && !unresolved, Truncated = truncated };
    }

    private static ExpandQuery Expansion(ImmutableArray<string> seeds, EdgeKind kinds, Direction direction, int depth) => new()
    {
        Seeds = seeds,
        EdgeKinds = [kinds],
        Direction = direction,
        MaxDepth = depth,
        MaxNodes = MaxNodes,
    };
}
