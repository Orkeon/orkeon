using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Core;

namespace Orkeon.Tools.Analysis.Tests;

public class IncrementalReindexToolTests
{
    // When no files changed (empty explicit list and no git diff provider), the
    // tool short-circuits to an "unchanged" result without touching the builder.
    private static Func<RaggableTreeBuilder> ThrowingFactory =>
        () => throw new InvalidOperationException("builder must not be invoked when nothing changed");

    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new IncrementalReindexTool(TestGraph.Store([], []), ThrowingFactory);
        Assert.Equal("incremental_reindex", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_unchanged_when_no_changed_files_and_no_git_diff()
    {
        using var tool = new IncrementalReindexTool(TestGraph.Store([], []), ThrowingFactory);

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
        using var tool = new IncrementalReindexTool(TestGraph.Store([], []), ThrowingFactory, gitDiff: null);

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
}
