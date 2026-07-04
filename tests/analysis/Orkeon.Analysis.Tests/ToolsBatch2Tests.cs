using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis;

namespace Orkeon.Analysis.Tests;

public class ToolsBatch2Tests
{
    private static async Task<(InMemoryRaggableStore Store, string Dir, BuildResult Result)> BuildAsync(
        (string Path, string Content)[] files)
    {
        var dir = await TestFixtures.WriteDirectoryAsync(files);
        var fs = TestFixtures.CreateFs(dir);
        var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
        var result = await builder.BuildAsync(TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
        return (new InMemoryRaggableStore(result.Tree, new FakeFileSystemService()), dir, result);
    }

    private static InMemoryRaggableStore BuildSyntheticStore()
    {
        var nodes = new List<RaggableNode>
        {
            Synthetic("a"), Synthetic("b"), Synthetic("c"), Synthetic("d"),
        };
        var edges = new List<RaggableEdge>
        {
            new("e1", "a", "b", EdgeKind.Calls),
            new("e2", "b", "c", EdgeKind.Calls),
            new("e3", "a", "d", EdgeKind.Calls),
        };
        return new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
    }

    [Fact]
    public async Task DependencyGraph_returns_nodes_edges_and_mermaid()
    {
        var (store, dir, _) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var tool = new DependencyGraphTool(store);
            var resp = await tool.ExecuteTypedForTest(
                new DependencyGraphRequest { Scope = NodeLevel.L2_Module, EdgeKinds = [EdgeKind.Imports], IncludeMermaid = true },
                CancellationToken.None);
            Assert.NotNull(resp.MermaidFlowchart);
            Assert.Contains("flowchart TD", resp.MermaidFlowchart);
            Assert.Equal(resp.Nodes.Length, resp.Metrics.NodeCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task SubGraph_expands_from_seeds_with_mermaid()
    {
        var store = BuildSyntheticStore();
        using var tool = new SubGraphTool(store);
        var resp = await tool.ExecuteTypedForTest(
            new SubGraphRequest { Seeds = ["a"], EdgeKinds = [EdgeKind.Calls], Depth = 2 },
            CancellationToken.None);
        Assert.Contains(resp.Nodes, n => n.Fqn == "a" && n.IsSeed);
        Assert.Contains(resp.Nodes, n => n.Fqn == "c");
        Assert.NotNull(resp.MermaidFlowchart);
    }

    [Fact]
    public async Task FlowTrace_shortest_path_returns_single_path()
    {
        var store = BuildSyntheticStore();
        using var tool = new FlowTraceTool(store);
        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "a", To = "c" },
            CancellationToken.None);
        Assert.Single(resp.Paths);
        Assert.Equal(2, resp.Paths[0].Length);
    }

    [Fact]
    public async Task FlowTrace_all_paths_enumerates_multiple()
    {
        var nodes = new List<RaggableNode> { Synthetic("a"), Synthetic("b"), Synthetic("c"), Synthetic("d") };
        var edges = new List<RaggableEdge>
        {
            new("e1", "a", "b", EdgeKind.Calls),
            new("e2", "a", "c", EdgeKind.Calls),
            new("e3", "b", "d", EdgeKind.Calls),
            new("e4", "c", "d", EdgeKind.Calls),
        };
        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        using var tool = new FlowTraceTool(store);
        var resp = await tool.ExecuteTypedForTest(
            new FlowTraceRequest { From = "a", To = "d", IncludeAllPaths = true, MaxDepth = 3, MaxPaths = 10 },
            CancellationToken.None);
        Assert.Equal(2, resp.Paths.Length);
    }

    [Fact]
    public async Task ImpactAnalysis_reports_direct_and_transitive()
    {
        var nodes = new List<RaggableNode> { Synthetic("target"), Synthetic("caller"), Synthetic("grandparent") };
        var edges = new List<RaggableEdge>
        {
            new("e1", "caller", "target", EdgeKind.Calls),
            new("e2", "grandparent", "caller", EdgeKind.Calls),
        };
        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        using var tool = new ImpactAnalysisTool(store);
        var resp = await tool.ExecuteTypedForTest(
            new ImpactAnalysisRequest { Target = "target", Direction = Direction.Backward, MaxDepth = 3 },
            CancellationToken.None);
        Assert.Contains("caller", resp.DirectImpact);
        Assert.Contains("grandparent", resp.TransitiveImpact);
    }

    [Fact]
    public async Task ComplexityReport_reports_top_with_percentiles()
    {
        var (store, dir, _) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.ComplexMethodTs),
            ("src/b.ts", TestFixtures.ActorProcessTs),
        ]);
        try
        {
            var tool = new ComplexityReportTool(store);
            var resp = await tool.ExecuteTypedForTest(
                new ComplexityReportRequest { Metric = ComplexityMetric.Cyclomatic, TopN = 10 },
                CancellationToken.None);
            Assert.NotEmpty(resp.Top);
            Assert.NotNull(resp.Median);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task StatementQuery_filters_by_kind()
    {
        var (store, dir, _) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.ComplexMethodTs),
        ]);
        try
        {
            var tool = new StatementQueryTool(store);
            var resp = await tool.ExecuteTypedForTest(
                new StatementQueryRequest { Kinds = [StatementKind.If, StatementKind.TryCatch] },
                CancellationToken.None);
            Assert.NotEmpty(resp.Hits);
            Assert.All(resp.Hits, h => Assert.Contains(h.Kind, new[] { StatementKind.If, StatementKind.TryCatch }));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static RaggableNode Synthetic(string fqn) => new()
    {
        Id = fqn,
        Kind = UniversalNodeKind.Function,
        Name = fqn,
        VirtualFilePath = $"/tmp/{fqn}.ts",
        Range = new NodeRange(0, 0, 0, 0, 0),
        Level = NodeLevel.L3_Symbol,
        Language = "typescript",
        SourceSnippet = "",
        Sha256 = "",
        Fqn = fqn,
    };
}
