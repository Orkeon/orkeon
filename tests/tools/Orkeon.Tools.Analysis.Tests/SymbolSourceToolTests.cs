using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Tests;

public class SymbolSourceToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new SymbolSourceTool(TestGraph.Store([], []));
        Assert.Equal("symbol_source", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Throws_fqn_not_found_for_unknown_symbol()
    {
        using var tool = new SymbolSourceTool(TestGraph.Store([], []));

        await Assert.ThrowsAsync<FqnNotFoundException>(() =>
            tool.ExecuteTypedForTest(
                new SymbolSourceRequest { Fqn = "/src::missing" },
                CancellationToken.None));
    }

    [Fact]
    public async Task Returns_source_citation_with_markdown_block()
    {
        var sym = TestGraph.Symbol(
            "/src::handle", "/src/a.ts",
            startLine: 1, endLine: 3,
            signature: "function handle()",
            source: "function handle() {\n  return 1;\n}");
        var store = TestGraph.Store([sym], []);
        using var tool = new SymbolSourceTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SymbolSourceRequest { Fqn = "/src::handle", Mode = SourceMode.SignatureAndBody },
            CancellationToken.None);

        Assert.Equal("/src::handle", resp.Fqn);
        Assert.Equal("/src/a.ts", resp.VirtualFilePath);
        Assert.False(resp.Stable); // no VFS-backed file -> snapshot is not verifiable
        Assert.Contains("// FQN  : /src::handle", resp.MarkdownReadyBlock);
        Assert.Contains("/src/a.ts:1-3", resp.MarkdownReadyBlock);
    }

    [Fact]
    public async Task Signature_only_mode_returns_signature_source()
    {
        var sym = TestGraph.Symbol(
            "/src::handle", "/src/a.ts",
            signature: "function handle()",
            source: "function handle() { return 1; }");
        var store = TestGraph.Store([sym], []);
        using var tool = new SymbolSourceTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new SymbolSourceRequest { Fqn = "/src::handle", Mode = SourceMode.SignatureOnly },
            CancellationToken.None);

        Assert.Equal("function handle()", resp.Source);
    }
}
