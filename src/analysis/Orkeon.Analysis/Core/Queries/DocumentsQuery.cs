using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

public sealed record DocumentingSection
{
    public string Path { get; init; } = string.Empty;
    public string HeadingPath { get; init; } = string.Empty;
    public int StartLine { get; init; }
}

public sealed record DocumentedSymbol
{
    public string Fqn { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string HeadingPath { get; init; } = string.Empty;
}

public sealed record DocumentedInResult
{
    public ImmutableList<DocumentingSection> Sections { get; init; } = [];
    public ImmutableList<string> Unresolved { get; init; } = [];
}

public sealed record DocumentsOfResult
{
    public ImmutableList<DocumentedSymbol> Symbols { get; init; } = [];
    public ImmutableList<string> Unresolved { get; init; } = [];
}

public static class DocumentsQuery
{
    private const int MaxNodes = 100_000;

    // Three reads whatever the number of sections: the symbol, its incoming Documents edges, their sections.
    public static async Task<DocumentedInResult> DocumentedInAsync(IRaggableStore store, string fqn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fqn);
        if (await store.GetAsync(fqn, ct).ConfigureAwait(false) is null) return new DocumentedInResult { Unresolved = [fqn] };

        var edges = await store.GetEdgesAsync(fqn, EdgeKind.Documents, Direction.Backward, ct).ConfigureAwait(false);
        var sections = await store.GetManyAsync(edges.Select(e => e.FromId).Distinct(StringComparer.Ordinal), ct).ConfigureAwait(false);
        return new DocumentedInResult
        {
            Sections =
            [
                .. sections.OrderBy(s => s.VirtualFilePath, StringComparer.Ordinal).ThenBy(s => s.Range.StartLine)
                    .Select(s => new DocumentingSection { Path = s.VirtualFilePath, HeadingPath = HeadingPath(s), StartLine = s.Range.StartLine }),
            ],
        };
    }

    // Two reads: the sections of the file (filtered by the store on the fqn prefix), then one expansion over all of them.
    public static async Task<DocumentsOfResult> DocumentsOfAsync(IRaggableStore store, string path, string rootVirtualPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(rootVirtualPath);
        var virtualPath = $"{rootVirtualPath.TrimEnd('/')}/{path.Replace('\\', '/')}";
        var candidates = await store.QueryAsync(
            new NodeQuery { Kinds = [UniversalNodeKind.Section], Packages = [virtualPath + "::"], Take = 1000 }, ct).ConfigureAwait(false);
        var sections = candidates.Where(n => n.VirtualFilePath == virtualPath).ToList();
        if (sections.Count == 0) return new DocumentsOfResult { Unresolved = [path] };

        var graph = await store.ExpandAsync(new ExpandQuery
        {
            Seeds = [.. sections.Select(s => s.Id)],
            EdgeKinds = [EdgeKind.Documents],
            Direction = Direction.Forward,
            MaxDepth = 1,
            MaxNodes = MaxNodes,
        }, ct).ConfigureAwait(false);
        var byId = sections.Concat(graph.Nodes).DistinctBy(n => n.Id, StringComparer.Ordinal).ToDictionary(n => n.Id, StringComparer.Ordinal);
        return new DocumentsOfResult
        {
            Symbols =
            [
                .. graph.Edges.Where(e => e.Kind == EdgeKind.Documents && byId.ContainsKey(e.FromId) && byId.ContainsKey(e.ToId))
                    .Select(e => (Section: byId[e.FromId], Symbol: byId[e.ToId]))
                    .OrderBy(x => x.Symbol.Fqn, StringComparer.Ordinal).ThenBy(x => x.Section.Fqn, StringComparer.Ordinal)
                    .Select(x => new DocumentedSymbol
                    {
                        Fqn = x.Symbol.Fqn,
                        Kind = x.Symbol.Kind.ToString(),
                        File = x.Symbol.VirtualFilePath,
                        HeadingPath = HeadingPath(x.Section),
                    }),
            ],
        };
    }

    private static string HeadingPath(RaggableNode section)
        => string.Join(" > ", section.Fqn[(section.Fqn.IndexOf("::", StringComparison.Ordinal) + 2)..].Split('/'));
}
