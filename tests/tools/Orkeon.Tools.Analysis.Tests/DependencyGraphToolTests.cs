using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Tests;

public class DependencyGraphToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new DependencyGraphTool(TestGraph.Store([], []));
        Assert.Equal("dependency_graph", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_empty_graph_when_store_is_empty()
    {
        using var tool = new DependencyGraphTool(TestGraph.Store([], []));

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L1_Package },
            CancellationToken.None);

        Assert.Empty(resp.Nodes);
        Assert.Empty(resp.Edges);
        Assert.Equal(0, resp.Metrics.NodeCount);
    }

    [Fact]
    public async Task Builds_package_import_graph_with_mermaid()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var pkgB = TestGraph.Package("root::b", "/src/b");
        var store = TestGraph.Store(
            [pkgA, pkgB],
            [TestGraph.Edge(pkgA, pkgB, EdgeKind.Imports)]);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest
            {
                Scope = NodeLevel.L1_Package,
                EdgeKinds = TestGraph.Arr(EdgeKind.Imports),
                IncludeMermaid = true,
            },
            CancellationToken.None);

        Assert.Equal(2, resp.Nodes.Length);
        Assert.Single(resp.Edges);
        Assert.Equal(2, resp.Metrics.NodeCount);
        Assert.NotNull(resp.MermaidFlowchart);
        Assert.Null(resp.Dot);
    }

    [Fact]
    public async Task Omits_external_edges_unless_requested()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var external = TestGraph.Package("root::ext", "/src/ext");
        // Edge to a node that is NOT included in the L1 query scope set is "external".
        var store = TestGraph.Store(
            [pkgA, external],
            [TestGraph.Edge(pkgA, external, EdgeKind.Imports)]);
        using var tool = new DependencyGraphTool(store);

        var withoutExternal = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest
            {
                Scope = NodeLevel.L1_Package,
                EdgeKinds = TestGraph.Arr(EdgeKind.Imports),
                RootFqn = "root::a",
                IncludeExternal = false,
            },
            CancellationToken.None);

        Assert.DoesNotContain(withoutExternal.Edges, e => e.To == external.Id);
    }
}
