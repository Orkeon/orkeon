using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

public class IsPathIndexedToolTests
{
    [Fact]
    public async Task Returns_false_when_store_is_empty()
    {
        var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
        using var tool = new IsPathIndexedTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new IsPathIndexedRequest { VirtualPath = "/src/app.ts" },
            CancellationToken.None);

        Assert.False(resp.Indexed);
        Assert.Null(resp.RootParent);
    }

    [Fact]
    public async Task Returns_true_when_descendant_of_root()
    {
        var store = BuildStoreWithRoots("/src");
        using var tool = new IsPathIndexedTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new IsPathIndexedRequest { VirtualPath = "/src/app/x.ts" },
            CancellationToken.None);

        Assert.True(resp.Indexed);
        Assert.Equal("/src", resp.RootParent);
        Assert.NotNull(resp.IndexedAt);
    }

    [Fact]
    public async Task Returns_true_when_exact_match_on_root()
    {
        var store = BuildStoreWithRoots("/src");
        using var tool = new IsPathIndexedTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new IsPathIndexedRequest { VirtualPath = "/src" },
            CancellationToken.None);

        Assert.True(resp.Indexed);
        Assert.Equal("/src", resp.RootParent);
    }

    [Fact]
    public async Task Returns_false_when_path_is_outside_coverage()
    {
        var store = BuildStoreWithRoots("/src");
        using var tool = new IsPathIndexedTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new IsPathIndexedRequest { VirtualPath = "/other/file.ts" },
            CancellationToken.None);

        Assert.False(resp.Indexed);
        Assert.Null(resp.RootParent);
    }

    [Fact]
    public async Task Returns_false_when_path_only_shares_prefix_string_but_not_directory()
    {
        var store = BuildStoreWithRoots("/src");
        using var tool = new IsPathIndexedTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new IsPathIndexedRequest { VirtualPath = "/src-other/file.ts" },
            CancellationToken.None);

        Assert.False(resp.Indexed);
    }

    [Fact]
    public async Task Overlapping_roots_resolve_to_longest_match()
    {
        var store = BuildStoreWithRoots("/src", "/src/app");
        using var tool = new IsPathIndexedTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new IsPathIndexedRequest { VirtualPath = "/src/app/x.ts" },
            CancellationToken.None);

        Assert.True(resp.Indexed);
        Assert.Equal("/src/app", resp.RootParent);
    }

    [Fact]
    public async Task Returns_false_for_empty_virtual_path()
    {
        var store = BuildStoreWithRoots("/src");
        using var tool = new IsPathIndexedTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new IsPathIndexedRequest { VirtualPath = "" },
            CancellationToken.None);

        Assert.False(resp.Indexed);
    }

    private static InMemoryRaggableStore BuildStoreWithRoots(params string[] virtualRoots)
    {
        var nodes = virtualRoots.Select(Monorepo).Cast<RaggableNode>().ToList();
        return new InMemoryRaggableStore(nodes, [], new FakeFileSystemService());
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
}
