using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

public class IncrementalReindexToolTests
{
    // When no files changed (empty explicit list and no git diff provider), the
    // tool short-circuits to an "unchanged" result without touching the engine.
    private static IndexFreshnessService ThrowingFreshness() => new(
        TestGraph.Store([], []),
        () => throw new InvalidOperationException("engine must not be invoked when nothing changed"),
        new FakeFileSystemService());

    [Fact]
    public void Has_stable_tool_contract()
    {
        using var freshness = ThrowingFreshness();
        using var tool = new IncrementalReindexTool(freshness);
        Assert.Equal("incremental_reindex", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_unchanged_when_no_changed_files_and_no_git_diff()
    {
        using var freshness = ThrowingFreshness();
        using var tool = new IncrementalReindexTool(freshness);

        var resp = await tool.ExecuteTypedForTest(
            new IncrementalReindexRequest { RootPath = "/src" },
            CancellationToken.None);

        Assert.Equal("unchanged", resp.IndexId);
        Assert.Equal(0, resp.NodeCount);
        Assert.Equal(0, resp.EdgeCount);
    }

    [Fact]
    public async Task Returns_unchanged_when_commits_are_set_but_no_git_diff_provider()
    {
        using var freshness = ThrowingFreshness();
        using var tool = new IncrementalReindexTool(freshness, gitDiff: null);

        var resp = await tool.ExecuteTypedForTest(
            new IncrementalReindexRequest
            {
                RootPath = "/src",
                FromCommit = "abc",
                ToCommit = "def",
            },
            CancellationToken.None);

        Assert.Equal("unchanged", resp.IndexId);
    }

    [Fact]
    [Trait("BR", "RT-30")]
    public async Task ShouldReportRealNodeAndEdgeCounts_WhenIncrementalReindexRuns()
    {
        var fs = new FakeFileSystemService().AddMount("/src", FileAccessRights.ReadWrite);
        fs.AddFile("/src/a.ts", "export class A { run(): number { return 1; } }");
        fs.AddFile("/src/b.ts", "export class B { run(): number { return 2; } }");
        fs.AddFile("/src/c.ts", "export class C { run(): number { return 3; } }");

        var initial = await new RaggableTreeBuilder(new TypeScriptAdapter(), fs).BuildAsync(
            "/src", new IndexCodebaseRequest { RootPath = "/src" }, CancellationToken.None);
        var store = new InMemoryRaggableStore(initial.Tree.Nodes, initial.Tree.Edges, fs);

        fs.AddFile("/src/a.ts", "export class A { run(): number { return 99; } }");

        using var freshness = new IndexFreshnessService(store, () => new IncrementalReindexEngine(new TypeScriptAdapter(), fs), fs);
        using var tool = new IncrementalReindexTool(freshness);

        var resp = await tool.ExecuteTypedForTest(
            new IncrementalReindexRequest { RootPath = "/src", ChangedFiles = ["/src/a.ts"] },
            CancellationToken.None);

        var (nodes, edges) = store.ExportSnapshot();
        Assert.True(nodes.Count > 0);
        Assert.Equal(nodes.Count, resp.NodeCount);
        Assert.Equal(edges.Count, resp.EdgeCount);
        Assert.Equal(1, resp.FileCount);
        Assert.True(resp.Elapsed > TimeSpan.Zero);
    }
}
