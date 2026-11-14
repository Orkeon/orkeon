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
    [Trait("BR", "#349")]
    public async Task Refuses_the_source_of_a_symbol_whose_file_is_outside_the_agent_scope()
    {
        var open = TestGraph.Symbol("/src::open", "/src/a/open.ts", source: "function open() {}");
        var secret = TestGraph.Symbol("/src::secret", "/src/b/secret.ts", source: "const key = 'secret';");
        using var tool = new SymbolSourceTool(TestGraph.Store([open, secret], []));
        await using var run = TestGraph.ScopedRun("/src/a");

        var refused = await tool.CallAsync(
            new Orkeon.Domain.Tools.Protocol.ToolCallRequest("symbol_source", new Dictionary<string, object?> { ["fqn"] = "/src::secret" }),
            TestContext.Current.CancellationToken);
        var served = await tool.CallAsync(
            new Orkeon.Domain.Tools.Protocol.ToolCallRequest("symbol_source", new Dictionary<string, object?> { ["fqn"] = "/src::open" }),
            TestContext.Current.CancellationToken);

        Assert.False(refused.Success);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(refused.Result), StringComparison.Ordinal);
        Assert.True(served.Success, served.Error);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldRefuseMaxLinesBelowOne_WhenValidated(int value)
    {
        using var tool = new SymbolSourceTool(TestGraph.Store([], []));

        var error = tool.ValidateTypedForTest(new SymbolSourceRequest { Fqn = "/src::a" } with { MaxLines = value });

        Assert.Equal($"max_lines must be at least 1 (got {value}); omit it for the default of 40", error);
    }
}
