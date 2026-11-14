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

    [Trait("BR", "#349")]
    [Fact]
    public async Task ShouldLeaveOutTheNodesAndEdgesOfAFileOutsideTheScope_WhenAScopedAgentExpands()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a/a.ts");
        var hidden = TestGraph.Symbol("/src::hidden", "/src/b/hidden.ts");
        using var tool = new SubGraphTool(TestGraph.Store([a, hidden], [TestGraph.Edge(a, hidden, EdgeKind.Calls)]));
        await using var run = TestGraph.ScopedRun("/src/a");
        using var call = Orkeon.Domain.Execution.AgentRunScope.EnterToolCall();

        var resp = await tool.ExecuteTypedForTest(new SubGraphRequest { Seeds = TestGraph.Arr("/src::a", "/src::hidden") }, CancellationToken.None);

        Assert.Equal(["/src::a"], resp.Nodes.Select(n => n.Fqn));
        Assert.Empty(resp.Edges);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldRefuseDepthBelowOne_WhenValidated(int value)
    {
        using var tool = new SubGraphTool(TestGraph.Store([], []));

        var error = tool.ValidateTypedForTest(new SubGraphRequest { Seeds = ["/src::a"] } with { Depth = value });

        Assert.Equal($"depth must be at least 1 (got {value}); omit it for the default of 2", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldRefuseMaxNodesBelowOne_WhenValidated(int value)
    {
        using var tool = new SubGraphTool(TestGraph.Store([], []));

        var error = tool.ValidateTypedForTest(new SubGraphRequest { Seeds = ["/src::a"] } with { MaxNodes = value });

        Assert.Equal($"max_nodes must be at least 1 (got {value}); omit it for the default of 100", error);
    }
}
