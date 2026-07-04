using Jint;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.Testing;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Testing;

public sealed class MockingTests
{
    private static (Engine engine, JsTestNamespace ns) NewEngineWithTest()
    {
        var engine = new JsEngineFactory().Create();
        var ns = TestNamespaceBinding.Register(engine);
        return (engine, ns);
    }

    [Fact]
    public async Task MockLlm_when_string_matcher_responds_with_configured_content()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("""
            test.mockLlm.when({ prompt: "hello" }).respond({ content: "hi back" });
            """, cancellationToken: TestContext.Current.CancellationToken);

        var resp = await ns.Llm.GenerateAsync("hello world", null, TestContext.Current.CancellationToken);

        Assert.Equal("hi back", resp.Content);
        Assert.Single(ns.Llm.GenerateCalls, c => c == "hello world");
    }

    [Fact]
    public async Task MockTool_when_args_match_returns_configured_response()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("""
            test.mockTool("fileRead").when({ path: "/data/test.txt" }).respond({ content: "mocked" });
            """, cancellationToken: TestContext.Current.CancellationToken);

        var tool = ns.GetMockTool("fileRead")!;
        var resp = await tool.CallAsync(new ToolCallRequest("fileRead",
            new Dictionary<string, object?> { ["path"] = "/data/test.txt" }), TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        var dict = (IDictionary<string, object?>)resp.Result!;
        Assert.Equal("mocked", dict["content"]!.ToString());
    }

    [Fact]
    public async Task assertLlmCalled_passes_when_call_count_matches()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("test.mockLlm.when({ prompt: 'x' }).respond({ content: 'y' });", cancellationToken: TestContext.Current.CancellationToken);
        await ns.Llm.GenerateAsync("x", null, TestContext.Current.CancellationToken);
        await ns.Llm.GenerateAsync("x", null, TestContext.Current.CancellationToken);

        Assert.Null(Record.Exception(() => engine.Evaluate("test.assertLlmCalled({ times: 2 });")));
    }

    [Fact]
    public async Task assertLlmCalled_throws_when_count_mismatches()
    {
        var (engine, ns) = NewEngineWithTest();
        await ns.Llm.GenerateAsync("ping", null, TestContext.Current.CancellationToken);

        var ex = ThrowsContaining<AssertionException>(
            () => engine.Evaluate("test.assertLlmCalled({ times: 5 });"),
            "expected 5");

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task assertToolCalled_passes_when_count_matches()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("test.mockTool('search').when({}).respond({ results: [] });", cancellationToken: TestContext.Current.CancellationToken);
        var tool = ns.GetMockTool("search")!;
        await tool.CallAsync(new ToolCallRequest("search", new Dictionary<string, object?>()), TestContext.Current.CancellationToken);

        Assert.Null(Record.Exception(() => engine.Evaluate("test.assertToolCalled('search', { times: 1 });")));
    }

    [Fact]
    public void assertToolCalled_throws_when_tool_unregistered()
    {
        var (engine, _) = NewEngineWithTest();

        var ex = ThrowsContaining<AssertionException>(
            () => engine.Evaluate("test.assertToolCalled('phantom');"),
            "phantom");

        Assert.NotNull(ex);
    }
}
