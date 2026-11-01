using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Tests;

public class FlowTraceToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new FlowTraceTool(TestGraph.Store([], []));
        Assert.Equal("flow_trace", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_no_paths_when_from_is_empty()
    {
        using var tool = new FlowTraceTool(TestGraph.Store([], []));

        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "" },
            CancellationToken.None);

        Assert.Empty(resp.Paths);
        Assert.False(resp.Truncated);
    }

    [Fact]
    public async Task Returns_no_paths_when_from_is_unknown()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var store = TestGraph.Store([a], []);
        using var tool = new FlowTraceTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "/src::missing", To = "/src::a" },
            CancellationToken.None);

        Assert.Empty(resp.Paths);
    }

    /// <summary>One path a -> m_i -> t per middle symbol: <paramref name="paths"/> distinct call paths from a to t.</summary>
    private static FlowTraceTool FanOut(int paths)
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var t = TestGraph.Symbol("/src::t", "/src/a.ts");
        var middles = Enumerable.Range(0, paths).Select(i => TestGraph.Symbol($"/src::m{i}", "/src/a.ts")).ToList();
        var edges = middles.SelectMany(m => new[] { TestGraph.Edge(a, m, EdgeKind.Calls), TestGraph.Edge(m, t, EdgeKind.Calls) }).ToList();
        return new FlowTraceTool(TestGraph.Store([a, t, .. middles], edges));
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldNotSayTruncated_WhenExactlyMaxPathsExist()
    {
        using var tool = FanOut(3);

        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "/src::a", To = "/src::t", IncludeAllPaths = true, MaxPaths = 3 }, CancellationToken.None);

        Assert.Equal(3, resp.Paths.Length);
        Assert.False(resp.Truncated);
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldSayTruncatedAndKeepMaxPaths_WhenOneMorePathExists()
    {
        using var tool = FanOut(4);

        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "/src::a", To = "/src::t", IncludeAllPaths = true, MaxPaths = 3 }, CancellationToken.None);

        Assert.Equal(3, resp.Paths.Length);
        Assert.True(resp.Truncated);
    }

    [Fact]
    public async Task Traces_shortest_call_path_from_caller_to_callee()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var b = TestGraph.Symbol("/src::b", "/src/a.ts");
        var store = TestGraph.Store([a, b], [TestGraph.Edge(a, b, EdgeKind.Calls)]);
        using var tool = new FlowTraceTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "/src::a", To = "/src::b" },
            CancellationToken.None);

        var path = Assert.Single(resp.Paths);
        Assert.Equal(2, path.Fqns.Length);
        Assert.Equal(a.Id, path.Fqns[0]);
        Assert.Equal(b.Id, path.Fqns[^1]);
    }

    [Fact]
    public async Task Enumerates_all_paths_to_target_within_depth_budget()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var b = TestGraph.Symbol("/src::b", "/src/a.ts");
        var c = TestGraph.Symbol("/src::c", "/src/a.ts");
        var store = TestGraph.Store(
            [a, b, c],
            [TestGraph.Edge(a, b, EdgeKind.Calls), TestGraph.Edge(b, c, EdgeKind.Calls)]);
        using var tool = new FlowTraceTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest
            {
                From = "/src::a",
                To = "/src::c",
                IncludeAllPaths = true,
                MaxDepth = 3,
            },
            CancellationToken.None);

        var path = Assert.Single(resp.Paths);
        Assert.Equal(3, path.Fqns.Length);
        Assert.Equal(c.Id, path.Fqns[^1]);
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldFollowExtendsAndImplements_WhenEdgeKindsNamesInherits()
    {
        var leaf = TestGraph.Symbol("/src::Leaf", "/src/a.ts", kind: UniversalNodeKind.Class);
        var middle = TestGraph.Symbol("/src::Middle", "/src/a.ts", kind: UniversalNodeKind.Class);
        var root = TestGraph.Symbol("/src::IRoot", "/src/a.ts", kind: UniversalNodeKind.Interface);
        var store = TestGraph.Store(
            [leaf, middle, root],
            [TestGraph.Edge(leaf, middle, EdgeKind.Extends), TestGraph.Edge(middle, root, EdgeKind.Implements)]);
        using var tool = new FlowTraceTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "/src::Leaf", To = "/src::IRoot", EdgeKinds = TestGraph.Arr("inherits") },
            CancellationToken.None);

        var path = Assert.Single(resp.Paths);
        Assert.Equal([leaf.Id, middle.Id, root.Id], path.Fqns);
    }

    [Fact]
    public async Task ShouldRefuseUnknownEdgeKindByName_WhenTracing()
    {
        using var tool = new FlowTraceTool(TestGraph.Store([], []));

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "/src::a", EdgeKinds = TestGraph.Arr("nonsense") },
            CancellationToken.None));

        Assert.Contains("nonsense", ex.Message, StringComparison.Ordinal);
    }
}
