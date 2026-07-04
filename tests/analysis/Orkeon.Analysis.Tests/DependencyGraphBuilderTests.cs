using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class DependencyGraphBuilderTests
{
    private static async Task<(List<RaggableNode> Nodes, IReadOnlyList<RaggableEdge> Edges)> BuildAsync(
        params (string Path, string Source)[] files)
    {
        using var pool = new TreeSitterParserPool();
        var adapter = new TypeScriptAdapter();
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

    [Fact]
    public async Task Adds_extends_edge()
    {
        var (_, edges) = await BuildAsync(("/r/a.ts", TestFixtures.Inheritance));
        Assert.Contains(edges, e => e.Kind == EdgeKind.Extends);
    }

    [Fact]
    public async Task Adds_implements_edge()
    {
        var (_, edges) = await BuildAsync(("/r/a.ts", TestFixtures.Inheritance));
        Assert.Contains(edges, e => e.Kind == EdgeKind.Implements);
    }

    [Fact]
    public async Task Inverse_links_are_symmetric_for_calls()
    {
        var (nodes, edges) = await BuildAsync(("/r/a.ts", TestFixtures.Calls));
        var callEdges = edges.Where(e => e.Kind == EdgeKind.Calls).ToList();
        Assert.NotEmpty(callEdges);
        foreach (var edge in callEdges)
        {
            var from = nodes.First(n => n.Id == edge.FromId);
            var to = nodes.FirstOrDefault(n => n.Id == edge.ToId);
            if (to is null) continue;
            Assert.Contains(edge.ToId, from.CallIds);
            Assert.Contains(edge.FromId, to.CalledByIds);
        }
    }

    [Fact]
    public async Task No_self_loop_in_call_edges()
    {
        var (_, edges) = await BuildAsync(("/r/a.ts", TestFixtures.Calls));
        Assert.DoesNotContain(edges, e => e.FromId == e.ToId);
    }

    [Fact]
    public async Task Resolves_relative_import_between_two_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "raggable-imports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utilsPath = Path.Combine(dir, "utils.ts");
            var mainPath = Path.Combine(dir, "main.ts");
            await File.WriteAllTextAsync(utilsPath, TestFixtures.Utils, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(mainPath, "import { Foo } from './utils';\nexport const x = new Foo();\n", TestContext.Current.CancellationToken);

            var (_, edges) = await BuildAsync(
                (mainPath, await File.ReadAllTextAsync(mainPath, TestContext.Current.CancellationToken)),
                (utilsPath, await File.ReadAllTextAsync(utilsPath, TestContext.Current.CancellationToken)));
            Assert.Contains(edges, e => e.Kind == EdgeKind.Imports);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task External_import_creates_external_target()
    {
        var (_, edges) = await BuildAsync(("/r/a.ts", TestFixtures.Imports));
        Assert.Contains(edges, e => e.Kind == EdgeKind.Imports && e.ToId.StartsWith("ext::npm::", StringComparison.Ordinal));
    }
}
