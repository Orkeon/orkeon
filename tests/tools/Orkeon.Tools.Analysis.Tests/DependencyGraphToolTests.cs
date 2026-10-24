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
                EdgeKinds = TestGraph.Arr("imports"),
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
                EdgeKinds = TestGraph.Arr("imports"),
                RootFqn = "root::a",
                IncludeExternal = false,
            },
            CancellationToken.None);

        Assert.DoesNotContain(withoutExternal.Edges, e => e.To == external.Id);
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public async Task ShouldFilterByWorkflowEdgeKind_WhenDependencyGraphAsksTests()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var pkgB = TestGraph.Package("root::b", "/src/b");
        var pkgC = TestGraph.Package("root::c", "/src/c");
        var store = TestGraph.Store(
            [pkgA, pkgB, pkgC],
            [
                TestGraph.Edge(pkgA, pkgB, EdgeKind.Tests),
                TestGraph.Edge(pkgA, pkgC, EdgeKind.ProjectDepends),
                TestGraph.Edge(pkgB, pkgC, EdgeKind.Imports),
            ]);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest
            {
                Scope = NodeLevel.L1_Package,
                EdgeKinds = TestGraph.Arr("tests"),
            },
            CancellationToken.None);

        var edge = Assert.Single(resp.Edges);
        Assert.Equal(EdgeKind.Tests, edge.Kind);
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public async Task ShouldAcceptLegacyEdgeKindName_WhenDependencyGraphAsksImports()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var pkgB = TestGraph.Package("root::b", "/src/b");
        var store = TestGraph.Store(
            [pkgA, pkgB],
            [
                TestGraph.Edge(pkgA, pkgB, EdgeKind.Imports),
                TestGraph.Edge(pkgA, pkgB, EdgeKind.Tests),
            ]);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest
            {
                Scope = NodeLevel.L1_Package,
                EdgeKinds = TestGraph.Arr("Imports"),
            },
            CancellationToken.None);

        var edge = Assert.Single(resp.Edges);
        Assert.Equal(EdgeKind.Imports, edge.Kind);
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public async Task ShouldFilterByWorkflowNodeKind_WhenDependencyGraphAsksProject()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var module = TestGraph.Module("/src/a/m.ts");
        var store = TestGraph.Store([pkgA, module], []);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest
            {
                Scope = NodeLevel.L1_Package,
                NodeKinds = TestGraph.Arr("project"),
            },
            CancellationToken.None);

        var node = Assert.Single(resp.Nodes);
        Assert.Equal("root::a", node.Fqn);
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public async Task ShouldHonourScope_WhenNodeKindsGiven()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var pkgB = TestGraph.Package("root::b", "/src/b");
        var module = TestGraph.Module("/src/a/m.ts");
        var store = TestGraph.Store([pkgA, pkgB, module], []);
        using var tool = new DependencyGraphTool(store);

        var atModule = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L2_Module, NodeKinds = TestGraph.Arr("project") },
            CancellationToken.None);
        var atPackage = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L1_Package, NodeKinds = TestGraph.Arr("project") },
            CancellationToken.None);
        var bounded = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L1_Package, NodeKinds = TestGraph.Arr("project"), MaxNodes = 1 },
            CancellationToken.None);

        Assert.Empty(atModule.Nodes);
        Assert.Equal(["root::a", "root::b"], atPackage.Nodes.Select(n => n.Fqn).Order().ToArray());
        Assert.Single(bounded.Nodes);
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public void ShouldAcceptRequestWithoutKinds_WhenParametersValidated()
    {
        var schema = Orkeon.Domain.Tools.ToolSchemaGenerator.GenerateSchema<DependencyGraphRequest>("dependency_graph", "d");

        Assert.False(schema.Parameters!["edge_kinds"].Required);
        Assert.False(schema.Parameters["node_kinds"].Required);
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public void ShouldAcceptRequestWithoutKinds_WhenSubGraphSchemaGenerated()
    {
        var schema = Orkeon.Domain.Tools.ToolSchemaGenerator.GenerateSchema<SubGraphRequest>("sub_graph", "d");

        Assert.False(schema.Parameters!["edge_kinds"].Required);
        Assert.False(schema.Parameters["node_kinds"].Required);
    }
}
