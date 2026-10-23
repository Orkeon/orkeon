using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.Fixtures.DevRepo;

namespace Orkeon.Analysis.Tests;

public sealed class TestEdgeLinkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "test-edge-" + Guid.NewGuid().ToString("N"));

    public TestEdgeLinkerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<RaggableTree> BuildAsync(DevRepoFixtureBuilder repo, ILanguageAdapter adapter)
    {
        IFileSystemService fs = TestFixtures.CreateFs(_dir);
        repo.Build(fs, TestFixtures.TestVirtualRoot);
        var builder = new RaggableTreeBuilder(adapter, fs);
        var result = await builder.BuildAsync(
            TestFixtures.TestVirtualRoot,
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
            CancellationToken.None);
        return result.Tree;
    }

    private static RaggableNode Method(RaggableTree tree, string name) =>
        Assert.Single(tree.Nodes, n => n.Kind == UniversalNodeKind.Method && n.Name == name);

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldMarkTestNode_WhenMethodHasFactOrTheoryAttribute()
    {
        const string source = """
            public class SampleTests
            {
                [Fact]
                public void FactCase() { }

                [Theory]
                public void TheoryCase(int x) { }

                public void Helper() { }
            }
            """;
        var tree = await BuildAsync(new DevRepoFixtureBuilder().WithCSharpFile("SampleTests.cs", source), new CSharpAdapter());

        var fact = Method(tree, "FactCase");
        var theory = Method(tree, "TheoryCase");
        var helper = Method(tree, "Helper");
        Assert.True(fact.Tags.ContainsKey("test"));
        Assert.True(theory.Tags.ContainsKey("test"));
        Assert.False(helper.Tags.ContainsKey("test"));
        Assert.All([fact, theory, helper], m => Assert.Equal(UniversalNodeKind.Method, m.Kind));
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldLinkTestToSymbolUnderTest_WhenCalledSymbolLivesInReferencedProject()
    {
        var repo = new DevRepoFixtureBuilder()
            .WithProject("A")
            .WithProject("B")
            .WithTestProject("T", "A")
            .WithCSharpFile("A/Alpha.cs", "public class Alpha { public static void Foo() { } }\n")
            .WithCSharpFile("B/Beta.cs", "public class Beta { public static void Bar() { } }\n")
            .WithCSharpFile("T/AlphaTests.cs", """
                public class AlphaTests
                {
                    [Fact]
                    public void Exercises()
                    {
                        Alpha.Foo();
                        Beta.Bar();
                    }
                }
                """);
        var tree = await BuildAsync(repo, new CSharpAdapter());

        var test = Method(tree, "Exercises");
        var foo = Method(tree, "Foo");
        var bar = Method(tree, "Bar");
        var edge = Assert.Single(tree.Edges, e => e.Kind == EdgeKind.Tests);
        Assert.Equal(test.Id, edge.FromId);
        Assert.Equal(foo.Id, edge.ToId);
        Assert.DoesNotContain(tree.Edges, e => e.Kind == EdgeKind.Tests && e.ToId == bar.Id);
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldMarkTestNode_WhenTypeScriptUsesTestOrIt()
    {
        const string source = """
            test("x", () => { sut(); });
            describe("suite", () => {
              it("y", () => { sut(); });
            });
            """;
        var tree = await BuildAsync(new DevRepoFixtureBuilder().WithTypeScriptFile("sample.spec.ts", source), new TypeScriptAdapter());

        var inFile = tree.Nodes.Where(n => n.VirtualFilePath == "/src/sample.spec.ts").ToList();
        var owner = Assert.Single(inFile, n => n.Level == NodeLevel.L2_Module);
        Assert.True(owner.Tags.ContainsKey("test"));
        Assert.DoesNotContain(inFile, n => n.Name is "x" or "y" or "suite");
        Assert.Single(inFile);
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldNotMarkTestNode_WhenTypeScriptCallsRegexTestOrMemberIt()
    {
        const string source = """
            function a(s: string) { return /re/.test(s); }
            function b(obj: any) { obj.it(); }
            class K { m() { this.test(); } }
            """;
        var tree = await BuildAsync(new DevRepoFixtureBuilder().WithTypeScriptFile("plain.ts", source), new TypeScriptAdapter());

        var inFile = tree.Nodes.Where(n => n.VirtualFilePath == "/src/plain.ts").ToList();
        Assert.NotEmpty(inFile);
        Assert.DoesNotContain(inFile, n => n.Tags.ContainsKey("test"));
    }

    [Fact]
    [Trait("BR", "R18")]
    public async Task ShouldLinkTestsEdge_WhenTsTestCallbackCallsASymbol()
    {
        var repo = new DevRepoFixtureBuilder()
            .WithTypeScriptFile("lib.ts", "export function helper() { return 1; }\n")
            .WithTypeScriptFile("lib.spec.ts", "import { helper } from './lib';\ntest('x', () => { helper(); });\n");
        var tree = await BuildAsync(repo, new TypeScriptAdapter());

        var module = Assert.Single(tree.Nodes, n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath == "/src/lib.spec.ts");
        var helper = Assert.Single(tree.Nodes, n => n.Name == "helper");
        Assert.True(module.Tags.ContainsKey("test"));
        var edge = Assert.Single(tree.Edges, e => e.Kind == EdgeKind.Tests);
        Assert.Equal(module.Id, edge.FromId);
        Assert.Equal(helper.Id, edge.ToId);
    }
}
