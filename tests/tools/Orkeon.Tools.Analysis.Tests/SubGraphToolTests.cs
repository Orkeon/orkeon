using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.Analysis.Tests;

public class SubGraphToolTests
{
    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldSerialiseTruncatedBeforeTheNodesAndMermaidLast_WhenCalledThroughTheToolPath()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var b = TestGraph.Symbol("/src::b", "/src/a.ts");
        using var tool = new SubGraphTool(TestGraph.Store([a, b], [TestGraph.Edge(a, b, EdgeKind.Calls)]));

        var response = await tool.CallAsync(
            new ToolCallRequest("sub_graph", new Dictionary<string, object?> { ["seeds"] = new List<object?> { "/src::a" }, ["include_mermaid"] = true }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var keys = Assert.IsType<Dictionary<string, object?>>(response.Result).Keys.ToList();
        Assert.Equal(["truncated", "nodes", "edges", "mermaid_flowchart"], keys);
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldLeaveMermaidOut_WhenIncludeMermaidIsNotAsked()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        using var tool = new SubGraphTool(TestGraph.Store([a], []));

        var resp = await tool.ExecuteTypedForTest(new SubGraphRequest { Seeds = TestGraph.Arr("/src::a") }, CancellationToken.None);

        Assert.Null(resp.MermaidFlowchart);
    }

    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new SubGraphTool(TestGraph.Store([], []));
        Assert.Equal("sub_graph", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_empty_subgraph_when_seeds_are_unknown()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var store = TestGraph.Store([a], []);
        using var tool = new SubGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SubGraphRequest { Seeds = TestGraph.Arr("/src::missing") },
            CancellationToken.None);

        Assert.Empty(resp.Nodes);
        Assert.Empty(resp.Edges);
    }

    [Fact]
    public async Task Expands_neighbors_from_seed_and_marks_seed()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var b = TestGraph.Symbol("/src::b", "/src/a.ts");
        var store = TestGraph.Store([a, b], [TestGraph.Edge(a, b, EdgeKind.Calls)]);
        using var tool = new SubGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SubGraphRequest
            {
                Seeds = TestGraph.Arr("/src::a"),
                EdgeKinds = TestGraph.Arr("calls"),
                Depth = 2,
                IncludeMermaid = true,
            },
            CancellationToken.None);

        Assert.Equal(2, resp.Nodes.Length);
        Assert.Single(resp.Edges);
        Assert.Contains(resp.Nodes, n => n.Fqn == "/src::a" && n.IsSeed);
        Assert.NotNull(resp.MermaidFlowchart);
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public async Task ShouldRefuseUnknownEdgeKindByName_WhenSubGraphAsked()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        using var tool = new SubGraphTool(TestGraph.Store([a], []));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => tool.ExecuteTypedForTest(
            new SubGraphRequest
            {
                Seeds = TestGraph.Arr("/src::a"),
                EdgeKinds = TestGraph.Arr("nonsense"),
            },
            CancellationToken.None));

        Assert.Contains("nonsense", ex.Message);
    }
}
