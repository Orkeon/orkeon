using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Analysis.Tests;

public class ComplexityReportToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new ComplexityReportTool(TestGraph.Store([], []));
        Assert.Equal("complexity_report", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_empty_report_with_null_percentiles_when_store_is_empty()
    {
        using var tool = new ComplexityReportTool(TestGraph.Store([], []));

        var resp = await tool.ExecuteTypedForTest(
            new ComplexityReportRequest { Metric = ComplexityMetric.Cyclomatic },
            CancellationToken.None);

        Assert.Equal(ComplexityMetric.Cyclomatic, resp.Metric);
        Assert.Empty(resp.Top);
        Assert.Null(resp.Median);
        Assert.Null(resp.P95);
        Assert.Equal(0, resp.EvaluatedCount);
    }

    [Fact]
    public async Task Ranks_methods_by_cyclomatic_complexity()
    {
        var simple = TestGraph.Symbol("/src::simple", "/src/a.ts", kind: UniversalNodeKind.Method);
        simple.StatementsMutable.Add(TestGraph.Statement("s1", simple.Id, StatementKind.Assignment, 2));

        var complex = TestGraph.Symbol("/src::complex", "/src/a.ts", kind: UniversalNodeKind.Method);
        complex.StatementsMutable.Add(TestGraph.Statement("c1", complex.Id, StatementKind.If, 2, condition: "x"));
        complex.StatementsMutable.Add(TestGraph.Statement("c2", complex.Id, StatementKind.For, 3));

        var store = TestGraph.Store([simple, complex], []);
        using var tool = new ComplexityReportTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new ComplexityReportRequest { Metric = ComplexityMetric.Cyclomatic, TopN = 10 },
            CancellationToken.None);

        Assert.Equal(2, resp.Top.Length);
        Assert.Equal("/src::complex", resp.Top[0].Fqn);
        Assert.True(resp.Top[0].Value >= resp.Top[1].Value);
        Assert.NotNull(resp.Median);
    }
}
