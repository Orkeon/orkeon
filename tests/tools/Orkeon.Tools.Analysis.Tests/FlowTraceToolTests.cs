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
}
