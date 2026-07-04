using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Tests;

public class StatementQueryToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new StatementQueryTool(TestGraph.Store([], []));
        Assert.Equal("statement_query", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_no_hits_when_store_is_empty()
    {
        using var tool = new StatementQueryTool(TestGraph.Store([], []));

        var resp = await tool.ExecuteTypedForTest(
            new StatementQueryRequest(),
            CancellationToken.None);

        Assert.Empty(resp.Hits);
        Assert.False(resp.Truncated);
    }

    [Fact]
    public async Task Returns_statements_for_a_named_parent()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts");
        sym.StatementsMutable.Add(TestGraph.Statement("s1", sym.Id, StatementKind.If, 2, condition: "ok"));
        sym.StatementsMutable.Add(TestGraph.Statement("s2", sym.Id, StatementKind.Return, 3));
        var store = TestGraph.Store([sym], []);
        using var tool = new StatementQueryTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new StatementQueryRequest { ParentFqns = TestGraph.Arr("/src::handle") },
            CancellationToken.None);

        Assert.Equal(2, resp.Hits.Length);
        Assert.All(resp.Hits, h => Assert.Equal("/src::handle", h.ParentFqn));
    }

    [Fact]
    public async Task Filters_statements_by_kind()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts");
        sym.StatementsMutable.Add(TestGraph.Statement("s1", sym.Id, StatementKind.If, 2, condition: "ok"));
        sym.StatementsMutable.Add(TestGraph.Statement("s2", sym.Id, StatementKind.Return, 3));
        var store = TestGraph.Store([sym], []);
        using var tool = new StatementQueryTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new StatementQueryRequest
            {
                ParentFqns = TestGraph.Arr("/src::handle"),
                Kinds = TestGraph.Arr(StatementKind.If),
            },
            CancellationToken.None);

        var hit = Assert.Single(resp.Hits);
        Assert.Equal(StatementKind.If, hit.Kind);
    }
}
