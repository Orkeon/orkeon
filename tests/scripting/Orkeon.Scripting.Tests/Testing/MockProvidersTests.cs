using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.Testing;

namespace Orkeon.Scripting.Tests.Testing;

public sealed class MockProvidersTests
{
    private static (Engine engine, JsTestNamespace ns) NewEngineWithTest()
    {
        var engine = new JsEngineFactory().Create();
        var ns = TestNamespaceBinding.Register(engine);
        return (engine, ns);
    }

    [Fact]
    public void MockLlmProvider_Name_is_mock()
    {
        Assert.Equal("mock", new MockLlmProvider().Name);
    }

    [Fact]
    public async Task MockLlmProvider_GenerateAsync_without_expectation_returns_empty()
    {
        var provider = new MockLlmProvider();

        var resp = await provider.GenerateAsync("anything", null, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, resp.Content);
        Assert.Single(provider.GenerateCalls);
        Assert.Equal(1, provider.TotalCalls);
    }

    [Fact]
    public async Task MockLlmProvider_ChatAsync_matches_last_user_message()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("test.mockLlm.when({ prompt: 'weather' }).respond({ content: 'sunny' });", cancellationToken: TestContext.Current.CancellationToken);

        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "be helpful" },
            new LlmMessage { Role = "user", Content = "what is the weather?" },
        };
        var resp = await ns.Llm.ChatAsync(messages, null, TestContext.Current.CancellationToken);

        Assert.Equal("sunny", resp.Content);
        Assert.Single(ns.Llm.ChatCalls);
    }

    [Fact]
    public async Task MockLlmProvider_ChatAsync_without_user_message_returns_empty()
    {
        var provider = new MockLlmProvider();

        var resp = await provider.ChatAsync(new[]
        {
            new LlmMessage { Role = "system", Content = "x" },
        }, null, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, resp.Content);
        Assert.Single(provider.ChatCalls);
    }

    [Fact]
    public async Task MockLlmProvider_TotalCalls_counts_generate_and_chat()
    {
        var provider = new MockLlmProvider();

        await provider.GenerateAsync("a", null, TestContext.Current.CancellationToken);
        await provider.ChatAsync(new[] { new LlmMessage { Role = "user", Content = "b" } }, null, TestContext.Current.CancellationToken);

        Assert.Equal(2, provider.TotalCalls);
    }

    [Fact]
    public async Task MockLlm_regex_matcher_responds()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("test.mockLlm.when({ prompt: /he..o/ }).respond('matched');", cancellationToken: TestContext.Current.CancellationToken);

        var resp = await ns.Llm.GenerateAsync("hello there", null, TestContext.Current.CancellationToken);

        Assert.Equal("matched", resp.Content);
    }

    [Fact]
    public async Task MockLlm_default_matcher_matches_any_prompt()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("test.mockLlm.when({}).respond('always');", cancellationToken: TestContext.Current.CancellationToken);

        var resp = await ns.Llm.GenerateAsync("literally anything", null, TestContext.Current.CancellationToken);

        Assert.Equal("always", resp.Content);
    }

    [Fact]
    public void MockTool_constructor_rejects_blank_name()
    {
        Assert.Throws<ArgumentException>(() => new MockTool("  "));
    }

    [Fact]
    public void MockTool_default_description_derives_from_name()
    {
        var tool = new MockTool("scan");

        Assert.Equal("mock tool 'scan'", tool.Description);
        Assert.Equal("scan", tool.Schema.Name);
    }

    [Fact]
    public void MockTool_custom_description_is_kept()
    {
        var tool = new MockTool("scan", "custom desc");

        Assert.Equal("custom desc", tool.Description);
    }

    [Fact]
    public async Task MockTool_CallAsync_records_and_returns_default_response()
    {
        var tool = new MockTool("t") { Response = "default" };

        var resp = await tool.CallAsync(new ToolCallRequest("t", new Dictionary<string, object?>()), TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        Assert.Equal("default", resp.Result);
        Assert.Single(tool.Calls);
    }

    [Fact]
    public async Task MockTool_expectation_falls_through_to_default_when_no_match()
    {
        var (engine, ns) = NewEngineWithTest();
        await engine.EvaluateAsync("""
            const h = test.mockTool('t');
            h.when({ mode: 'fast' }).respond('matched');
            h.respond('fallback');
            """, cancellationToken: TestContext.Current.CancellationToken);
        var tool = ns.GetMockTool("t")!;

        var resp = await tool.CallAsync(new ToolCallRequest("t",
            new Dictionary<string, object?> { ["mode"] = "slow" }), TestContext.Current.CancellationToken);

        Assert.Equal("fallback", resp.Result);
    }

    [Fact]
    public async Task MockTool_ExecuteAsync_returns_response_string()
    {
        var tool = new MockTool("t") { Response = "out" };

        var result = await tool.ExecuteAsync("ignored", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("out", result.Output);
    }

    [Fact]
    public async Task MockTool_ExecuteAsync_with_null_response_returns_empty()
    {
        var tool = new MockTool("t");

        var result = await tool.ExecuteAsync("x", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void MockTool_ValidateInput_always_true()
    {
        var tool = new MockTool("t");

        Assert.True(tool.ValidateInput("anything"));
        Assert.True(tool.ValidateInput(""));
    }

    [Fact]
    public void GetMockTool_returns_null_for_unknown()
    {
        var (_, ns) = NewEngineWithTest();

        Assert.Null(ns.GetMockTool("ghost"));
    }
}
