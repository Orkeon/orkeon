using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.Fixtures.DevRepo;

namespace Orkeon.Analysis.Tests;

public sealed class ProjectGraphBuilderTests : IDisposable
{
    private const string Class = "public class Widget { public void Run() { } }\n";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "project-graph-" + Guid.NewGuid().ToString("N"));

    public ProjectGraphBuilderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<RaggableTree> BuildAsync(DevRepoFixtureBuilder repo)
    {
        IFileSystemService fs = TestFixtures.CreateFs(_dir);
        repo.Build(fs, TestFixtures.TestVirtualRoot);
        var builder = new RaggableTreeBuilder(new CSharpAdapter(), fs);
        var result = await builder.BuildAsync(
            TestFixtures.TestVirtualRoot,
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
            CancellationToken.None);
        return result.Tree;
    }

    private static RaggableNode ProjectNode(RaggableTree tree, string name) =>
        Assert.Single(tree.Nodes, n => n.Kind == UniversalNodeKind.Package && n.Name == name);

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldEmitProjectNodes_WhenCsprojFilesDiscovered()
    {
        var tree = await BuildAsync(new DevRepoFixtureBuilder()
            .WithProject("Core")
            .WithTestProject("Core.Tests", "Core"));

        var projects = tree.Nodes.Where(n => n.Kind == UniversalNodeKind.Package).ToList();
        Assert.Equal(["Core", "Core.Tests"], projects.Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(projects, p => Assert.Equal("csproj", p.Tags["marker"]));
        Assert.Equal(
            ["/src/Core", "/src/Core.Tests"],
            projects.Select(p => p.VirtualFilePath).Order(StringComparer.Ordinal).ToArray());
        var monorepo = Assert.Single(tree.Nodes, n => n.Level == NodeLevel.L0_Monorepo);
        Assert.All(projects, p =>
        {
            Assert.Equal(monorepo.Id, p.ParentId);
            Assert.Single(tree.Edges, e => e.Kind == EdgeKind.Contains && e.FromId == monorepo.Id && e.ToId == p.Id);
        });
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldEmitProjectDependsEdges_WhenCsprojHasProjectReference()
    {
        var tree = await BuildAsync(new DevRepoFixtureBuilder()
            .WithProject("B")
            .WithProject("A", "B"));

        var a = ProjectNode(tree, "A");
        var b = ProjectNode(tree, "B");
        var depends = tree.Edges.Where(e => e.Kind == EdgeKind.ProjectDepends).ToList();
        var edge = Assert.Single(depends);
        Assert.Equal(a.Id, edge.FromId);
        Assert.Equal(b.Id, edge.ToId);
        Assert.DoesNotContain(depends, e => e.FromId == b.Id);
    }

    [Fact]
    [Trait("BR", "RT-33")]
    public async Task ShouldContainFilesInProject_WhenFileUnderProjectFolder()
    {
        var tree = await BuildAsync(new DevRepoFixtureBuilder()
            .WithProject("A")
            .WithProject("B")
            .WithCSharpFile("A/Deep/Nested/Widget.cs", Class)
            .WithCSharpFile("B/Gadget.cs", Class.Replace("Widget", "Gadget", StringComparison.Ordinal)));

        var a = ProjectNode(tree, "A");
        var b = ProjectNode(tree, "B");
        var widget = Assert.Single(tree.Nodes, n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath == "/src/A/Deep/Nested/Widget.cs");
        var gadget = Assert.Single(tree.Nodes, n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath == "/src/B/Gadget.cs");

        Assert.Equal(a.Id, widget.ParentId);
        Assert.Equal(b.Id, gadget.ParentId);
        Assert.Single(tree.Edges, e => e.Kind == EdgeKind.Contains && e.FromId == a.Id && e.ToId == widget.Id);
        Assert.Single(tree.Edges, e => e.Kind == EdgeKind.Contains && e.FromId == b.Id && e.ToId == gadget.Id);
        Assert.DoesNotContain(tree.Edges, e => e.Kind == EdgeKind.Contains && e.FromId == b.Id && e.ToId == widget.Id);
    }
}
