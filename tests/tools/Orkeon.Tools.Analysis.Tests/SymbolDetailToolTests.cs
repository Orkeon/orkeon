using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Analysis.Tests;

public class SymbolDetailToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new SymbolDetailTool(TestGraph.Store([], []));
        Assert.Equal("symbol_detail", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Throws_fqn_not_found_for_unknown_symbol()
    {
        var sym = TestGraph.Symbol("/src::known", "/src/a.ts");
        var store = TestGraph.Store([sym], []);
        using var tool = new SymbolDetailTool(store);

        await Assert.ThrowsAsync<FqnNotFoundException>(() =>
            tool.ExecuteTypedForTest(
                new SymbolDetailRequest { Fqn = "/src::missing" },
                CancellationToken.None));
    }

    [Fact]
    public async Task Returns_signature_and_doc_when_requested()
    {
        var sym = TestGraph.Symbol(
            "/src::handle", "/src/a.ts",
            kind: UniversalNodeKind.Method,
            signature: "handle(): void",
            doc: "Handles the request.");
        var store = TestGraph.Store([sym], []);
        using var tool = new SymbolDetailTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SymbolDetailRequest
            {
                Fqn = "/src::handle",
                IncludeSignature = true,
                IncludeDoc = true,
            },
            CancellationToken.None);

        Assert.Equal("/src::handle", resp.Fqn);
        Assert.Equal(UniversalNodeKind.Method, resp.Kind);
        Assert.Equal("handle(): void", resp.Signature);
        Assert.Equal("Handles the request.", resp.DocComment);
        Assert.Empty(resp.Members);
    }

    [Fact]
    public async Task Expands_members_and_truncates_at_max_children()
    {
        var parent = TestGraph.Symbol("/src::Svc", "/src/a.ts", kind: UniversalNodeKind.Class);
        var m1 = TestGraph.Symbol("/src::Svc::a", "/src/a.ts", kind: UniversalNodeKind.Method, parentId: parent.Id);
        var m2 = TestGraph.Symbol("/src::Svc::b", "/src/a.ts", kind: UniversalNodeKind.Method, parentId: parent.Id);
        TestGraph.Link(parent, m1, m2);
        var store = TestGraph.Store([parent, m1, m2], []);
        using var tool = new SymbolDetailTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SymbolDetailRequest
            {
                Fqn = "/src::Svc",
                Expand = ExpandModes.Members,
                MaxChildren = 1,
            },
            CancellationToken.None);

        Assert.Single(resp.Members);
        Assert.True(resp.Truncated);
    }

    [Fact]
    public async Task Resolves_callers_and_callees_when_expanded()
    {
        var caller = TestGraph.Symbol("/src::caller", "/src/a.ts", kind: UniversalNodeKind.Method);
        var subject = TestGraph.Symbol("/src::subject", "/src/a.ts", kind: UniversalNodeKind.Method);
        var callee = TestGraph.Symbol("/src::callee", "/src/a.ts", kind: UniversalNodeKind.Method);
        TestGraph.RecordCall(caller, subject);
        TestGraph.RecordCall(subject, callee);
        var store = TestGraph.Store([caller, subject, callee], []);
        using var tool = new SymbolDetailTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SymbolDetailRequest
            {
                Fqn = "/src::subject",
                Expand = ExpandModes.Callers | ExpandModes.Callees,
            },
            CancellationToken.None);

        Assert.Contains(resp.TopCallers, c => c.Fqn == "/src::caller");
        Assert.Contains(resp.TopCallees, c => c.Fqn == "/src::callee");
    }

    [Fact]
    public async Task Omits_signature_and_doc_when_not_requested()
    {
        var sym = TestGraph.Symbol(
            "/src::handle", "/src/a.ts",
            signature: "handle(): void",
            doc: "doc");
        var store = TestGraph.Store([sym], []);
        using var tool = new SymbolDetailTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SymbolDetailRequest
            {
                Fqn = "/src::handle",
                IncludeSignature = false,
                IncludeDoc = false,
            },
            CancellationToken.None);

        Assert.Null(resp.Signature);
        Assert.Null(resp.DocComment);
    }
}
