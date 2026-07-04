using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

public class IndexStatusToolTests
{
    [Fact]
    public async Task Returns_empty_list_when_store_is_empty()
    {
        var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
        using var tool = new IndexStatusTool(store);

        var resp = await tool.ExecuteTypedForTest(new IndexStatusRequest(), CancellationToken.None);

        Assert.Empty(resp.Roots);
    }

    [Fact]
    public async Task Returns_single_root_with_node_edge_counts_and_language_summary()
    {
        var nodes = new List<RaggableNode>
        {
            Monorepo("/src"),
            Module("/src", "/src/a.ts", "typescript"),
            Module("/src", "/src/b.ts", "typescript"),
            Module("/src", "/src/main.py", "python"),
        };
        LinkChildren(nodes[0], nodes.Skip(1));
        var edges = new List<RaggableEdge>
        {
            new("e1", nodes[1].Id, nodes[2].Id, EdgeKind.Imports, null, null),
        };

        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        using var tool = new IndexStatusTool(store);
        var resp = await tool.ExecuteTypedForTest(new IndexStatusRequest(), CancellationToken.None);

        var root = Assert.Single(resp.Roots);
        Assert.Equal("/src", root.VirtualRoot);
        Assert.Equal(4, root.NodeCount);
        Assert.Equal(1, root.EdgeCount);
        Assert.NotNull(root.LanguageSummary);
        Assert.Contains("typescript: 2 files", root.LanguageSummary);
        Assert.Contains("python: 1 file", root.LanguageSummary);
    }

    [Fact]
    public async Task Returns_multiple_roots_when_two_monorepos_coexist()
    {
        var nodes = new List<RaggableNode>
        {
            Monorepo("/src"),
            Module("/src", "/src/a.ts", "typescript"),
            Monorepo("/other"),
            Module("/other", "/other/b.ts", "typescript"),
        };
        LinkChildren(nodes[0], [nodes[1]]);
        LinkChildren(nodes[2], [nodes[3]]);

        var store = new InMemoryRaggableStore(nodes, [], new FakeFileSystemService());
        using var tool = new IndexStatusTool(store);
        var resp = await tool.ExecuteTypedForTest(new IndexStatusRequest(), CancellationToken.None);

        Assert.Equal(2, resp.Roots.Count);
        Assert.Contains(resp.Roots, r => r.VirtualRoot == "/src");
        Assert.Contains(resp.Roots, r => r.VirtualRoot == "/other");
    }

    [Fact]
    public async Task Populates_indexed_at_to_recent_utc_timestamp()
    {
        var nodes = new List<RaggableNode> { Monorepo("/src") };
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var store = new InMemoryRaggableStore(nodes, [], new FakeFileSystemService());
        using var tool = new IndexStatusTool(store);
        var resp = await tool.ExecuteTypedForTest(new IndexStatusRequest(), CancellationToken.None);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var root = Assert.Single(resp.Roots);
        Assert.InRange(root.IndexedAt, before, after);
    }

    private static RaggableNode Monorepo(string virtualRoot) => new()
    {
        Id = $"monorepo::{virtualRoot}",
        Kind = UniversalNodeKind.Monorepo,
        Name = virtualRoot,
        Fqn = virtualRoot,
        VirtualFilePath = virtualRoot,
        Range = new NodeRange(0, 0, 0, 0, 0),
        Level = NodeLevel.L0_Monorepo,
        Language = "multi",
        SourceSnippet = string.Empty,
        Sha256 = string.Empty,
    };

    private static RaggableNode Module(string root, string virtualFilePath, string language) => new()
    {
        Id = $"{virtualFilePath}#module@0",
        Kind = UniversalNodeKind.Module,
        Name = virtualFilePath,
        Fqn = virtualFilePath,
        VirtualFilePath = virtualFilePath,
        Range = new NodeRange(0, 0, 1, 2, 0),
        Level = NodeLevel.L2_Module,
        Language = language,
        SourceSnippet = string.Empty,
        Sha256 = string.Empty,
        ParentId = $"monorepo::{root}",
    };

    private static void LinkChildren(RaggableNode parent, IEnumerable<RaggableNode> children)
    {
        foreach (var child in children) parent.ChildrenIdsMutable.Add(child.Id);
    }
}
