using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.Fixtures.DevRepo;

namespace Orkeon.Analysis.Tests;

public sealed class ProjectGraphIncrementalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "project-graph-incr-" + Guid.NewGuid().ToString("N"));

    public ProjectGraphIncrementalTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<(IFileSystemService Fs, RaggableTree Tree)> BuildAsync(DevRepoFixtureBuilder repo)
    {
        IFileSystemService fs = TestFixtures.CreateFs(_dir);
        repo.Build(fs, TestFixtures.TestVirtualRoot);
        var builder = new RaggableTreeBuilder(new CSharpAdapter(), fs);
        var result = await builder.BuildAsync(
            TestFixtures.TestVirtualRoot,
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
            CancellationToken.None);
        return (fs, result.Tree);
    }

    private static Task<IncrementalReindexResult> ReindexAsync(IFileSystemService fs, RaggableTree tree, string virtualPath) =>
        new IncrementalReindexEngine(new CSharpAdapter(), fs).ReindexAsync(
            tree, TestFixtures.TestVirtualRoot, [virtualPath],
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
            CancellationToken.None);

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldRefreshProjectDependsEdges_WhenCsprojEditedAndReindexedIncrementally()
    {
        var (fs, tree) = await BuildAsync(new DevRepoFixtureBuilder().WithProject("A").WithProject("B"));
        Assert.DoesNotContain(tree.Edges, e => e.Kind == EdgeKind.ProjectDepends);

        var csproj = $"{TestFixtures.TestVirtualRoot}/A/A.csproj";
        await fs.WriteAllTextAsync(
            csproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <ProjectReference Include=\"..\\B\\B.csproj\" />\n  </ItemGroup>\n</Project>\n",
            CancellationToken.None);

        var result = await ReindexAsync(fs, tree, csproj);

        var a = Assert.Single(result.Tree.Nodes, n => n.Kind == UniversalNodeKind.Package && n.Name == "A");
        var b = Assert.Single(result.Tree.Nodes, n => n.Kind == UniversalNodeKind.Package && n.Name == "B");
        var edge = Assert.Single(result.Tree.Edges, e => e.Kind == EdgeKind.ProjectDepends);
        Assert.Equal(a.Id, edge.FromId);
        Assert.Equal(b.Id, edge.ToId);
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldLinkTestToSymbolUnderTest_WhenTestFileEditedAndReindexedIncrementally()
    {
        var (fs, tree) = await BuildAsync(new DevRepoFixtureBuilder()
            .WithProject("A")
            .WithTestProject("T", "A")
            .WithCSharpFile("A/Alpha.cs", "public class Alpha { public static void Foo() { } }\n")
            .WithCSharpFile("T/AlphaTests.cs", "public class AlphaTests { public void Exercises() { } }\n"));
        Assert.DoesNotContain(tree.Edges, e => e.Kind == EdgeKind.Tests);

        var testFile = $"{TestFixtures.TestVirtualRoot}/T/AlphaTests.cs";
        await fs.WriteAllTextAsync(
            testFile,
            "public class AlphaTests { [Fact] public void Exercises() { Alpha.Foo(); } }\n",
            CancellationToken.None);

        var result = await ReindexAsync(fs, tree, testFile);

        var test = Assert.Single(result.Tree.Nodes, n => n.Kind == UniversalNodeKind.Method && n.Name == "Exercises");
        var foo = Assert.Single(result.Tree.Nodes, n => n.Kind == UniversalNodeKind.Method && n.Name == "Foo");
        Assert.True(test.Tags.ContainsKey("test"));
        var edge = Assert.Single(result.Tree.Edges, e => e.Kind == EdgeKind.Tests);
        Assert.Equal(test.Id, edge.FromId);
        Assert.Equal(foo.Id, edge.ToId);
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldDropTestsEdges_WhenCsprojRemovesProjectReferenceIncrementally()
    {
        var (fs, tree) = await BuildAsync(new DevRepoFixtureBuilder()
            .WithProject("A")
            .WithTestProject("T", "A")
            .WithCSharpFile("A/Alpha.cs", "public class Alpha { public static void Foo() { } }\n")
            .WithCSharpFile("T/AlphaTests.cs", "public class AlphaTests { [Fact] public void Exercises() { Alpha.Foo(); } }\n"));
        Assert.Single(tree.Edges, e => e.Kind == EdgeKind.Tests);

        var csproj = $"{TestFixtures.TestVirtualRoot}/T/T.csproj";
        await fs.WriteAllTextAsync(
            csproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>\n",
            CancellationToken.None);

        var result = await ReindexAsync(fs, tree, csproj);

        Assert.DoesNotContain(result.Tree.Edges, e => e.Kind == EdgeKind.Tests);
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldEmitContainsEdge_WhenFileAddedIncrementally()
    {
        var (fs, tree) = await BuildAsync(new DevRepoFixtureBuilder().WithProject("A"));
        var added = $"{TestFixtures.TestVirtualRoot}/A/Added.cs";
        await fs.WriteAllTextAsync(added, "public class Added { }\n", CancellationToken.None);

        var result = await ReindexAsync(fs, tree, added);

        var pkg = Assert.Single(result.Tree.Nodes, n => n.Kind == UniversalNodeKind.Package && n.Name == "A");
        var module = Assert.Single(result.Tree.Nodes, n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath.EndsWith("/A/Added.cs"));
        Assert.Contains(result.Tree.Edges, e => e.Kind == EdgeKind.Contains && e.FromId == pkg.Id && e.ToId == module.Id);
    }
}
