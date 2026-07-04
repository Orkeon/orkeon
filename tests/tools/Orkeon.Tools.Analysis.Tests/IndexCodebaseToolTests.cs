using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Core;

namespace Orkeon.Tools.Analysis.Tests;

public class IndexCodebaseToolTests
{
    // The builder factory is only invoked for a valid root path. For the
    // contract paths under test (missing root_path), it must never be called,
    // so a throwing factory doubles as an assertion that the guard short-circuits.
    private static Func<RaggableTreeBuilder> ThrowingFactory =>
        () => throw new InvalidOperationException("builder must not be invoked for invalid input");

    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new IndexCodebaseTool(TestGraph.Store([], []), ThrowingFactory);
        Assert.Equal("index_codebase", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_validation_error_when_root_path_is_empty()
    {
        using var tool = new IndexCodebaseTool(TestGraph.Store([], []), ThrowingFactory);

        var resp = await tool.ExecuteTypedForTest(
            new IndexCodebaseRequest { RootPath = "" },
            CancellationToken.None);

        Assert.Equal(string.Empty, resp.IndexId);
        Assert.Equal(0, resp.NodeCount);
        Assert.NotEmpty(resp.Errors);
        Assert.Contains(resp.Errors, e => e.Contains("root_path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Returns_validation_error_when_root_path_is_whitespace()
    {
        using var tool = new IndexCodebaseTool(TestGraph.Store([], []), ThrowingFactory);

        var resp = await tool.ExecuteTypedForTest(
            new IndexCodebaseRequest { RootPath = "   " },
            CancellationToken.None);

        Assert.NotEmpty(resp.Errors);
        Assert.Equal(TimeSpan.Zero, resp.Elapsed);
    }
}
