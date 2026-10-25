using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

/// <summary>Runs one adapter over sources: its symbols, and the edges the dependency graph draws between them.</summary>
internal static class AdapterHarness
{
    public static IReadOnlyList<RaggableNode> Extract(ILanguageAdapter adapter, string path, string source)
    {
        using var pool = new TreeSitterParserPool();
        return new UniversalSemanticMapper(adapter, pool).ExtractNodes(path, source);
    }

    public static async Task<(List<RaggableNode> Nodes, IReadOnlyList<RaggableEdge> Edges)> GraphAsync(
        ILanguageAdapter adapter, params (string Path, string Source)[] files)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(adapter, pool);
        var builder = new DependencyGraphBuilder(adapter, pool);
        var all = new List<RaggableNode>();
        foreach (var (path, source) in files)
        {
            var nodes = mapper.ExtractNodes(path, source);
            all.AddRange(nodes);
            builder.RegisterFile(path, source, nodes);
        }

        var edges = await builder.BuildDependenciesAsync(new DefaultReferenceResolver(), CancellationToken.None);
        return (all, edges);
    }

    /// <summary>Whether an edge of <paramref name="kind"/> runs from the symbol (or module) named <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool HasEdge(List<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges, EdgeKind kind, string from, string to)
    {
        bool Named(string id, string name) => nodes.Any(n => n.Id == id && (n.Name == name || n.VirtualFilePath == name && n.Level == NodeLevel.L2_Module));
        return edges.Any(e => e.Kind == kind && Named(e.FromId, from) && Named(e.ToId, to));
    }
}
