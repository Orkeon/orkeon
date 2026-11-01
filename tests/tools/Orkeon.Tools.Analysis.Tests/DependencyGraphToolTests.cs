using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.Analysis.Tests;

public class DependencyGraphToolTests
{
    private static Orkeon.Analysis.Core.InMemoryRaggableStore PackagesStore(int count)
    {
        var packages = Enumerable.Range(0, count).Select(i => TestGraph.Package($"root::p{i:D3}", $"/src/p{i:D3}")).ToList();
        return TestGraph.Store(packages, []);
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldSerialiseTruncatedAndTheCountsBeforeTheNodes_WhenCalledThroughTheToolPath()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var pkgB = TestGraph.Package("root::b", "/src/b");
        using var tool = new DependencyGraphTool(TestGraph.Store([pkgA, pkgB], [TestGraph.Edge(pkgA, pkgB, EdgeKind.Imports)]));

        var response = await tool.CallAsync(
            new ToolCallRequest("dependency_graph", new Dictionary<string, object?> { ["scope"] = "L1_Package", ["include_mermaid"] = true }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var keys = Assert.IsType<Dictionary<string, object?>>(response.Result).Keys.ToList();
        Assert.Equal(["truncated", "cycles_truncated", "metrics", "cycles", "nodes", "edges", "mermaid_flowchart"], keys);
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldLeaveMermaidOut_WhenIncludeMermaidIsNotAsked()
    {
        using var tool = new DependencyGraphTool(PackagesStore(2));

        var resp = await tool.ExecuteTypedForTest(new DependencyGraphRequest { Scope = NodeLevel.L1_Package }, CancellationToken.None);

        Assert.Null(resp.MermaidFlowchart);
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldNotSayTruncated_WhenTheScopeHoldsExactlyMaxNodes()
    {
        using var tool = new DependencyGraphTool(PackagesStore(3));

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L1_Package, MaxNodes = 3 }, CancellationToken.None);

        Assert.Equal(3, resp.Nodes.Length);
        Assert.False(resp.Truncated);
    }

    [Trait("BR", "#283")]
    [Fact]
    public async Task ShouldSayTruncated_WhenTheScopeHoldsOneNodeMoreThanMaxNodes()
    {
        using var tool = new DependencyGraphTool(PackagesStore(4));

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L1_Package, MaxNodes = 3 }, CancellationToken.None);

        Assert.Equal(3, resp.Nodes.Length);
        Assert.True(resp.Truncated);
    }

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

    [Fact]
    public async Task ShouldNameCycleMembers_WhenModulesImportEachOther()
    {
        var a = TestGraph.Module("/src/a.ts");
        var b = TestGraph.Module("/src/b.ts");
        var c = TestGraph.Module("/src/c.ts");
        var store = TestGraph.Store(
            [a, b, c],
            [TestGraph.Edge(a, b, EdgeKind.Imports), TestGraph.Edge(b, a, EdgeKind.Imports), TestGraph.Edge(b, c, EdgeKind.Imports)]);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L2_Module },
            CancellationToken.None);

        var cycle = Assert.Single(resp.Cycles);
        Assert.Equal(["/src/a.ts", "/src/b.ts"], cycle.Fqns.ToArray());
        Assert.Equal(2, cycle.MemberCount);
        Assert.Equal(EdgeKind.Imports, cycle.EdgeKind);
        Assert.Equal(1, resp.Metrics.CycleCount);
        Assert.False(resp.CyclesTruncated);
    }

    [Fact]
    public async Task ShouldBoundCycles_WhenMaxCyclesAndMembersGiven()
    {
        var nodes = Enumerable.Range(0, 8).Select(i => TestGraph.Module($"/src/m{i}.ts")).ToArray();
        var edges = new List<Orkeon.Analysis.Abstractions.Models.RaggableEdge>
        {
            // a three-member cycle 0 -> 1 -> 2 -> 0
            TestGraph.Edge(nodes[0], nodes[1], EdgeKind.Imports),
            TestGraph.Edge(nodes[1], nodes[2], EdgeKind.Imports),
            TestGraph.Edge(nodes[2], nodes[0], EdgeKind.Imports),
            // two two-member cycles
            TestGraph.Edge(nodes[3], nodes[4], EdgeKind.Imports),
            TestGraph.Edge(nodes[4], nodes[3], EdgeKind.Imports),
            TestGraph.Edge(nodes[5], nodes[6], EdgeKind.Imports),
            TestGraph.Edge(nodes[6], nodes[5], EdgeKind.Imports),
        };
        using var tool = new DependencyGraphTool(TestGraph.Store(nodes, edges));

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L2_Module, MaxCycles = 2, MaxCycleMembers = 2 },
            CancellationToken.None);

        Assert.Equal(3, resp.Metrics.CycleCount);
        Assert.Equal(2, resp.Cycles.Length);
        Assert.True(resp.CyclesTruncated);
        // the largest cycle comes first, its members cut to the cap
        Assert.Equal(3, resp.Cycles[0].MemberCount);
        Assert.Equal(2, resp.Cycles[0].Fqns.Length);
        Assert.Equal(2, resp.Cycles[1].MemberCount);
    }

    [Fact]
    public async Task ShouldFollowImportsForCycles_WhenEdgeKindsEmpty()
    {
        var a = TestGraph.Module("/src/a.ts");
        var b = TestGraph.Module("/src/b.ts");
        var c = TestGraph.Module("/src/c.ts");
        var store = TestGraph.Store(
            [a, b, c],
            [
                TestGraph.Edge(a, b, EdgeKind.Imports), TestGraph.Edge(b, a, EdgeKind.Imports),
                TestGraph.Edge(b, c, EdgeKind.Calls), TestGraph.Edge(c, b, EdgeKind.Calls),
            ]);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L2_Module, EdgeKinds = [] },
            CancellationToken.None);

        // the graph follows imports only, so the cycles do too: the calls between b and c form no cycle
        Assert.All(resp.Edges, e => Assert.Equal(EdgeKind.Imports, e.Kind));
        var cycle = Assert.Single(resp.Cycles);
        Assert.Equal(["/src/a.ts", "/src/b.ts"], cycle.Fqns.ToArray());
        Assert.Equal(EdgeKind.Imports, cycle.EdgeKind);
    }

    [Fact]
    public async Task ShouldLabelCycleWithEveryKindFollowed_WhenSeveralEdgeKindsGiven()
    {
        var a = TestGraph.Module("/src/a.ts");
        var b = TestGraph.Module("/src/b.ts");
        var store = TestGraph.Store(
            [a, b],
            [TestGraph.Edge(a, b, EdgeKind.Imports), TestGraph.Edge(b, a, EdgeKind.Calls)]);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L2_Module, EdgeKinds = TestGraph.Arr("imports", "calls") },
            CancellationToken.None);

        Assert.Equal(EdgeKind.Imports | EdgeKind.Calls, Assert.Single(resp.Cycles).EdgeKind);
    }

    [Fact]
    public async Task ShouldCountCyclesOverWholeScope_WhenNodesCapped()
    {
        var a = TestGraph.Module("/src/a.ts");
        var b = TestGraph.Module("/src/b.ts");
        var store = TestGraph.Store(
            [a, b],
            [TestGraph.Edge(a, b, EdgeKind.Imports), TestGraph.Edge(b, a, EdgeKind.Imports)]);
        using var tool = new DependencyGraphTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L2_Module, MaxNodes = 1 },
            CancellationToken.None);

        Assert.Equal(1, resp.Metrics.NodeCount);
        Assert.Equal(1, resp.Metrics.CycleCount);
        Assert.Equal(2, Assert.Single(resp.Cycles).Fqns.Length);
        Assert.Contains("whole scope", tool.Description, StringComparison.Ordinal);
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


    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldReturnTheRootsNodes_WhenTheyLieAfterTheFirstPageOfTheScope()
    {
        var others = Enumerable.Range(0, 5).Select(i => TestGraph.Package($"root::p{i:D3}", $"/src/p{i:D3}")).ToList();
        var zz = TestGraph.Package("root::zz", "/zz");
        var zzA = TestGraph.Package("root::zz::a", "/zz/a");
        var zzB = TestGraph.Package("root::zz::b", "/zz/b");
        using var tool = new DependencyGraphTool(TestGraph.Store([.. others, zz, zzA, zzB], []));

        var cut = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L1_Package, RootFqn = "root::zz", MaxNodes = 2 }, CancellationToken.None);
        var whole = await tool.ExecuteTypedForTest(
            new DependencyGraphRequest { Scope = NodeLevel.L1_Package, RootFqn = "root::zz", MaxNodes = 3 }, CancellationToken.None);

        Assert.Equal(["root::zz", "root::zz::a"], cut.Nodes.Select(n => n.Fqn));
        Assert.True(cut.Truncated);
        Assert.Equal(3, whole.Nodes.Length);
        Assert.False(whole.Truncated);
    }
}
