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
    public async Task CSharp_object_creation_calls_the_type_it_creates()
    {
        const string source = """
            namespace Demo;
            public class Engine { }
            public class Car
            {
                public Engine Build() { return new Engine(); }
            }
            """;
        var (nodes, edges) = await AdapterHarness.GraphAsync(new CSharpAdapter(), ("/r/Car.cs", source));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Calls, "Build", "Engine"));
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
    public async Task ShouldAddOneImportEdge_WhenAFileImportsAModuleTwice()
    {
        var (_, edges) = await BuildAsync(("/r/a.ts", "import { a } from 'lodash';\nimport { b } from 'lodash';\nexport const x = a(b);\n"));

        var imports = edges.Where(e => e.Kind == EdgeKind.Imports).ToList();
        Assert.Single(imports);
        Assert.Equal(edges.Count, edges.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ShouldResolveTheBaseClassOfItsOwnFile_WhenTwoFilesDefineTheName()
    {
        var (nodes, edges) = await BuildAsync(
            ("/r/a.ts", "export class Base { }\nexport class Left extends Base { }\n"),
            ("/r/b.ts", "export class Base { }\nexport class Right extends Base { }\n"),
            ("/r/c.ts", "export class Shape { }\n"),
            ("/r/d.ts", "export class Circle extends Shape { }\n"));

        string FileOf(string id) => nodes.Single(n => n.Id == id).VirtualFilePath;
        var extends = edges.Where(e => e.Kind == EdgeKind.Extends).ToDictionary(e => nodes.Single(n => n.Id == e.FromId).Name, e => e.ToId);
        Assert.Equal("/r/a.ts", FileOf(extends["Left"]));
        Assert.Equal("/r/b.ts", FileOf(extends["Right"]));
        // A name defined in another file only still resolves, through the table built once for every file.
        Assert.Equal("/r/c.ts", FileOf(extends["Circle"]));
        Assert.Contains(extends["Circle"], nodes.Single(n => n.Name == "Circle").ExtendsIds);
    }

    [Fact]
    public async Task External_import_creates_external_target()
    {
        var (_, edges) = await BuildAsync(("/r/a.ts", TestFixtures.Imports));
        Assert.Contains(edges, e => e.Kind == EdgeKind.Imports && e.ToId.StartsWith("ext::npm::", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "R18")]
    public async Task ShouldCollectCall_WhenCallSitsInTopLevelTestCallback()
    {
        const string source = "export function helper() { return 1; }\ntest('x', () => { helper(); });\n";

        var (nodes, edges) = await BuildAsync(("/r/a.spec.ts", source));

        var module = Assert.Single(nodes, n => n.Level == NodeLevel.L2_Module);
        var helper = Assert.Single(nodes, n => n.Name == "helper");
        var edge = Assert.Single(edges, e => e.Kind == EdgeKind.Calls && e.ToId == helper.Id);
        Assert.Equal(module.Id, edge.FromId);
    }
}
