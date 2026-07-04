using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Tests;

public class ImpactAnalysisToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new ImpactAnalysisTool(TestGraph.Store([], []));
        Assert.Equal("impact_analysis", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Throws_fqn_not_found_for_unknown_target()
    {
        var a = TestGraph.Symbol("/src::a", "/src/a.ts");
        var store = TestGraph.Store([a], []);
        using var tool = new ImpactAnalysisTool(store);

        await Assert.ThrowsAsync<FqnNotFoundException>(() =>
            tool.ExecuteTypedForTest(
                new ImpactAnalysisRequest { Target = "/src::missing" },
                CancellationToken.None));
    }

    [Fact]
    public async Task Reports_direct_callers_when_walking_backwards()
    {
        // caller -> target (caller Calls target)
        var caller = TestGraph.Symbol("pkg::mod::caller", "/src/a.ts");
        var target = TestGraph.Symbol("pkg::mod::target", "/src/a.ts");
        TestGraph.RecordCall(caller, target);
        var store = TestGraph.Store(
            [caller, target],
            [TestGraph.Edge(caller, target, EdgeKind.Calls)]);
        using var tool = new ImpactAnalysisTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new ImpactAnalysisRequest { Target = "pkg::mod::target", Direction = Direction.Backward },
            CancellationToken.None);

        Assert.Equal("pkg::mod::target", resp.Target);
        Assert.Contains(caller.Id, resp.DirectImpact);
        Assert.Contains(resp.TopCallers, c => c.Fqn == "pkg::mod::caller");
    }

    [Fact]
    public async Task Reports_transitive_impact_grouped_by_package()
    {
        // grand -> mid -> target (each Calls the next); backward impact of target
        // should surface both mid (direct) and grand (transitive).
        var grand = TestGraph.Symbol("pkg::a::grand", "/src/a.ts");
        var mid = TestGraph.Symbol("pkg::a::mid", "/src/a.ts");
        var target = TestGraph.Symbol("pkg::a::target", "/src/a.ts");
        TestGraph.RecordCall(grand, mid);
        TestGraph.RecordCall(mid, target);
        var store = TestGraph.Store(
            [grand, mid, target],
            [TestGraph.Edge(grand, mid, EdgeKind.Calls), TestGraph.Edge(mid, target, EdgeKind.Calls)]);
        using var tool = new ImpactAnalysisTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new ImpactAnalysisRequest { Target = "pkg::a::target", Direction = Direction.Backward },
            CancellationToken.None);

        Assert.Contains(mid.Id, resp.DirectImpact);
        Assert.Contains("pkg::a::grand", resp.TransitiveImpact);
        Assert.NotEmpty(resp.ByPackage);
    }

    [Fact]
    public async Task Returns_empty_impact_for_leaf_symbol_with_no_callers()
    {
        var leaf = TestGraph.Symbol("/src::leaf", "/src/a.ts");
        var store = TestGraph.Store([leaf], []);
        using var tool = new ImpactAnalysisTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new ImpactAnalysisRequest { Target = "/src::leaf" },
            CancellationToken.None);

        Assert.Empty(resp.DirectImpact);
        Assert.Empty(resp.TransitiveImpact);
        Assert.Empty(resp.TopCallers);
    }
}
