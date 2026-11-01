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
    public async Task ShouldDrawImplements_WhenACSharpClassNamesAnInterfaceInItsBaseList()
    {
        const string source = """
            namespace Demo;
            public interface IShape { }
            public interface IDrawable : IShape { }
            public abstract class ShapeBase { }
            public class Circle : ShapeBase, IDrawable { }
            public struct Point : IShape { }
            public record Size : IShape;
            """;
        var (nodes, edges) = await AdapterHarness.GraphAsync(new CSharpAdapter(), ("/r/Shapes.cs", source));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "Circle", "ShapeBase"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Implements, "Circle", "IDrawable"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Implements, "Point", "IShape"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Implements, "Size", "IShape"));
        // An interface naming an interface extends it, as in Java and TypeScript.
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "IDrawable", "IShape"));
        Assert.False(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "Circle", "IDrawable"));
        Assert.Contains(nodes.Single(n => n.Name == "IDrawable").Id, nodes.Single(n => n.Name == "Circle").ImplementsIds);
    }

    [Fact]
    public async Task ShouldLinkTheBaseOfTheOwnersNamespace_WhenTwoNamespacesDeclareTheShortName()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(
            new CSharpAdapter(),
            ("/r/A/Options.cs", "namespace Alpha;\npublic class Options { }\n"),
            ("/r/B/Options.cs", "namespace Beta;\npublic class Options { }\n"),
            ("/r/B/Derived.cs", "namespace Beta;\npublic class BetaOptions : Options { }\n"),
            ("/r/B/Inner/Nested.cs", "namespace Beta.Inner;\npublic class InnerOptions : Options { }\n"));

        string TargetFile(string from) => nodes.Single(n => n.Id == edges.Single(e => e.Kind == EdgeKind.Extends
            && nodes.Single(f => f.Id == e.FromId).Name == from).ToId).VirtualFilePath;
        Assert.Equal("/r/B/Options.cs", TargetFile("BetaOptions"));
        // Beta.Inner sits inside Beta: the enclosing namespace's type is the one C# resolves.
        Assert.Equal("/r/B/Options.cs", TargetFile("InnerOptions"));
    }

    [Fact]
    public async Task ShouldLinkTheBaseOfTheOwnersNamespace_WhenNamespacesAreBlocks()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(
            new CSharpAdapter(),
            ("/r/A.cs", "namespace Alpha { public class Options { } }\nnamespace Beta { public class Options { } }\n"),
            ("/r/B.cs", "namespace Other { }\nnamespace Beta { public class BetaOptions : Options { } }\n"));

        var edge = Assert.Single(edges, e => e.Kind == EdgeKind.Extends);
        Assert.EndsWith("Beta::Options", nodes.Single(n => n.Id == edge.ToId).Fqn, StringComparison.Ordinal);
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldDrawNoEdge_WhenTwoNamespacesDeclareTheShortNameAndNoneIsEnclosingOrUsed()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(
            new CSharpAdapter(),
            ("/r/A/Options.cs", "namespace Alpha;\npublic class Options { }\n"),
            ("/r/B/Options.cs", "namespace Beta;\npublic class Options { }\n"),
            ("/r/C/Consumer.cs", "namespace Gamma;\npublic class GammaOptions : Options { }\n"));

        var owner = nodes.Single(n => n.Name == "GammaOptions");
        Assert.DoesNotContain(edges, e => e.FromId == owner.Id && e.Kind is EdgeKind.Extends or EdgeKind.Implements);
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldLinkTheBaseOfTheUsedNamespace_WhenTwoNamespacesDeclareTheShortName()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(
            new CSharpAdapter(),
            ("/r/A/Options.cs", "namespace Alpha;\npublic class Options { }\n"),
            ("/r/B/Options.cs", "namespace Beta;\npublic class Options { }\n"),
            ("/r/C/Consumer.cs", "using Beta;\nnamespace Gamma;\npublic class GammaOptions : Options { }\n"));

        var owner = nodes.Single(n => n.Name == "GammaOptions");
        var edge = Assert.Single(edges, e => e.FromId == owner.Id && e.Kind == EdgeKind.Extends);
        Assert.Equal("/r/B/Options.cs", nodes.Single(n => n.Id == edge.ToId).VirtualFilePath);
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldLinkTheImportedBase_WhenTwoModulesDeclareTheShortName()
    {
        var (nodes, edges) = await BuildAsync(
            ("/r/a.ts", "export class Base { }\n"),
            ("/r/b.ts", "export class Base { }\n"),
            ("/r/c.ts", "import { Base } from './b';\nexport class Derived extends Base { }\n"));

        var owner = nodes.Single(n => n.Name == "Derived");
        var edge = Assert.Single(edges, e => e.FromId == owner.Id && e.Kind == EdgeKind.Extends);
        Assert.Equal("/r/b.ts::Base", nodes.Single(n => n.Id == edge.ToId).Fqn);
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldDrawNoEdge_WhenTwoModulesDeclareTheShortNameAndNeitherIsImported()
    {
        var (nodes, edges) = await BuildAsync(
            ("/r/a.ts", "export class Base { }\n"),
            ("/r/b.ts", "export class Base { }\n"),
            ("/r/c.ts", "import { Other } from './d';\nexport class Derived extends Base { }\n"),
            ("/r/d.ts", "export class Other { }\n"));

        var owner = nodes.Single(n => n.Name == "Derived");
        Assert.DoesNotContain(edges, e => e.FromId == owner.Id && e.Kind is EdgeKind.Extends or EdgeKind.Implements);
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldLinkTheOnlyDeclaration_WhenOneModuleDeclaresTheShortName()
    {
        var (nodes, edges) = await BuildAsync(
            ("/r/a.ts", "export class Base { }\n"),
            ("/r/c.ts", "export class Derived extends Base { }\n"));

        var owner = nodes.Single(n => n.Name == "Derived");
        var edge = Assert.Single(edges, e => e.FromId == owner.Id && e.Kind == EdgeKind.Extends);
        Assert.Equal("/r/a.ts::Base", nodes.Single(n => n.Id == edge.ToId).Fqn);
    }

    [Fact]
    public async Task ShouldLinkTheOnlyDeclaration_WhenOneNamespaceDeclaresTheShortName()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(
            new CSharpAdapter(),
            ("/r/A/Options.cs", "namespace Alpha;\npublic class Options { }\n"),
            ("/r/C/Consumer.cs", "using Alpha;\nnamespace Gamma;\npublic class GammaOptions : Options { }\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "GammaOptions", "Options"));
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


    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldResolveTheGlobalType_WhenAnotherNamespaceDeclaresTheSameName()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(
            new CSharpAdapter(),
            ("/r/Global.cs", "public class Options { }\n"),
            ("/r/Alpha.cs", "namespace Alpha;\npublic class Options { }\n"),
            ("/r/Gamma.cs", "namespace Gamma;\npublic class G : Options { }\n"));

        var g = nodes.Single(n => n.Name == "G");
        var extends = Assert.Single(edges, e => e.Kind == EdgeKind.Extends && e.FromId == g.Id);
        Assert.Equal("/r/Global.cs", nodes.Single(n => n.Id == extends.ToId).VirtualFilePath);
    }
}
