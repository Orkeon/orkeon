using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Analysis.Tests;

public class CodebaseMapToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new CodebaseMapTool(TestGraph.Store([], []));
        Assert.Equal("codebase_map", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_empty_map_when_store_is_empty()
    {
        using var tool = new CodebaseMapTool(TestGraph.Store([], []));

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L2_Module },
            CancellationToken.None);

        Assert.Equal(NodeLevel.L2_Module, resp.Level);
        Assert.Empty(resp.Entries);
        Assert.Equal(0, resp.Totals.NodeCount);
        Assert.False(resp.Truncated);
    }

    [Fact]
    public async Task Lists_modules_at_requested_level_with_totals()
    {
        var monorepo = TestGraph.Monorepo();
        var a = TestGraph.Module("/src/a.ts");
        var b = TestGraph.Module("/src/b.ts");
        TestGraph.Link(monorepo, a, b);
        var store = TestGraph.Store([monorepo, a, b], []);
        using var tool = new CodebaseMapTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L2_Module },
            CancellationToken.None);

        Assert.Equal(2, resp.Entries.Length);
        Assert.Equal(2, resp.Totals.ModuleCount);
        Assert.All(resp.Entries, e => Assert.Equal(UniversalNodeKind.Module, e.Kind));
    }

    [Fact]
    public async Task Scopes_entries_to_root_fqn_subtree()
    {
        var monorepo = TestGraph.Monorepo();
        var inScope = TestGraph.Module("/src/app/a.ts", parentId: monorepo.Id);
        var outScope = TestGraph.Module("/lib/b.ts", parentId: monorepo.Id);
        var appPkg = TestGraph.Package("root::app", "/src/app", parentId: monorepo.Id);
        TestGraph.Link(monorepo, appPkg, inScope, outScope);
        var store = TestGraph.Store([monorepo, appPkg, inScope, outScope], []);
        using var tool = new CodebaseMapTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L2_Module, RootFqn = "root::app" },
            CancellationToken.None);

        Assert.Single(resp.Entries);
        Assert.Equal("/src/app/a.ts", resp.Entries[0].Fqn);
    }

    [Fact]
    public async Task Includes_metrics_when_requested()
    {
        var monorepo = TestGraph.Monorepo();
        var method = TestGraph.Symbol("/src::m", "/src/a.ts", kind: UniversalNodeKind.Method, startLine: 1, endLine: 10);
        method.StatementsMutable.Add(TestGraph.Statement("s1", method.Id, StatementKind.If, 2, condition: "x"));
        TestGraph.Link(monorepo, method);
        var store = TestGraph.Store([monorepo, method], []);
        using var tool = new CodebaseMapTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L3_Symbol, IncludeMetrics = true },
            CancellationToken.None);

        var entry = Assert.Single(resp.Entries);
        Assert.NotNull(entry.Cyclomatic);
        Assert.Equal(10, entry.LoC);
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldNotSayTruncated_WhenTheLevelHoldsExactlyMaxEntries()
    {
        var monorepo = TestGraph.Monorepo();
        var a = TestGraph.Module("/src/a.ts");
        var b = TestGraph.Module("/src/b.ts");
        TestGraph.Link(monorepo, a, b);
        using var tool = new CodebaseMapTool(TestGraph.Store([monorepo, a, b], []));

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L2_Module, MaxEntries = 2 }, CancellationToken.None);

        Assert.Equal(2, resp.Entries.Length);
        Assert.False(resp.Truncated);
    }

    [Fact]
    public async Task Truncates_when_more_nodes_than_max_entries()
    {
        var monorepo = TestGraph.Monorepo();
        var a = TestGraph.Module("/src/a.ts");
        var b = TestGraph.Module("/src/b.ts");
        TestGraph.Link(monorepo, a, b);
        var store = TestGraph.Store([monorepo, a, b], []);
        using var tool = new CodebaseMapTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L2_Module, MaxEntries = 1 },
            CancellationToken.None);

        Assert.Single(resp.Entries);
        Assert.True(resp.Truncated);
    }


    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldReturnTheRootsEntries_WhenTheyLieAfterTheFirstPageOfTheLevel()
    {
        var others = Enumerable.Range(0, 5).Select(i => TestGraph.Module($"/a/m{i}.ts")).ToList();
        var zz = TestGraph.Package("root::zz", "/zz");
        var x = TestGraph.Module("/zz/x.ts");
        var y = TestGraph.Module("/zz/y.ts");
        using var tool = new CodebaseMapTool(TestGraph.Store([.. others, zz, x, y], []));

        var cut = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L2_Module, RootFqn = "root::zz", MaxEntries = 1 }, CancellationToken.None);
        var whole = await tool.ExecuteTypedForTest(
            new CodebaseMapRequest { Level = NodeLevel.L2_Module, RootFqn = "root::zz", MaxEntries = 2 }, CancellationToken.None);

        Assert.Equal(["/zz/x.ts"], cut.Entries.Select(e => e.Fqn));
        Assert.True(cut.Truncated);
        Assert.Equal(["/zz/x.ts", "/zz/y.ts"], whole.Entries.Select(e => e.Fqn));
        Assert.False(whole.Truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldRefuseMaxEntriesBelowOne_WhenValidated(int value)
    {
        using var tool = new CodebaseMapTool(TestGraph.Store([], []));

        var error = tool.ValidateTypedForTest(new CodebaseMapRequest { Level = NodeLevel.L2_Module } with { MaxEntries = value });

        Assert.Equal($"max_entries must be at least 1 (got {value}); omit it for the default of 200", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void ShouldAcceptMaxEntriesFromOne_WhenValidated(int value)
    {
        using var tool = new CodebaseMapTool(TestGraph.Store([], []));

        var error = tool.ValidateTypedForTest(new CodebaseMapRequest { Level = NodeLevel.L2_Module } with { MaxEntries = value });

        Assert.Null(error);
    }
}
