using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class LlmFacadeTests
{
    private static (Engine, StubLlmProvider) NewFacade(out JsLlmFacade facade)
    {
        var engine = new Engine(opt => JsEngineFactory.ApplyInteropPolicy(opt));
        var provider = new StubLlmProvider();
        facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        return (engine, provider);
    }

    [Fact]
    public async Task complete_returns_provider_text()
    {
        var (_, provider) = NewFacade(out var facade);
        provider.RespondTo(p => p == "hi" ? new LlmResponse { Content = "world" } : new LlmResponse());

        var result = await facade.complete("hi", null);

        Assert.Equal("world", result);
    }

    [Fact]
    public async Task complete_falls_back_to_undefined_llm_when_no_provider_is_injected()
    {
        using var engine = new Engine(opt => JsEngineFactory.ApplyInteropPolicy(opt));
        var facade = new JsLlmFacade(engine, null, CancellationToken.None);

        var result = await facade.complete("ping", null);

        Assert.StartsWith("<undefined-llm:", result);
    }

    [Fact]
    public async Task chat_returns_object_with_content_and_tokens()
    {
        var (engine, provider) = NewFacade(out var facade);
        provider.RespondToChatWith(new LlmResponse { Content = "ok", TokensUsed = 12 });

        var messages = await engine.EvaluateAsync("[{role:'user', content:'hi'}]", cancellationToken: TestContext.Current.CancellationToken);
        var resp = await facade.chat(messages, null);

        Assert.Equal("ok", resp.Get("content").AsString());
        Assert.Equal(12d, Convert.ToDouble(resp.Get("tokensUsed").ToObject()));
    }

    [Fact]
    public async Task extract_parses_JSON_response_into_object()
    {
        var (engine, provider) = NewFacade(out var facade);
        provider.RespondWith(new LlmResponse { Content = "{\"name\":\"Ada\",\"age\":36}" });

        var schema = await engine.EvaluateAsync("({type:'object'})", cancellationToken: TestContext.Current.CancellationToken);
        var result = await facade.extract("Parse this", schema, null);

        Assert.Equal("Ada", result.Get("name").AsString());
        Assert.Equal(36d, Convert.ToDouble(result.Get("age").ToObject()));
    }

    [Fact]
    public async Task extract_strips_markdown_fences_before_parsing()
    {
        var (engine, provider) = NewFacade(out var facade);
        provider.RespondWith(new LlmResponse { Content = "```json\n{\"name\":\"Ada\"}\n```" });

        var schema = await engine.EvaluateAsync("({type:'object'})", cancellationToken: TestContext.Current.CancellationToken);
        var result = await facade.extract("Parse this", schema, null);

        Assert.Equal("Ada", result.Get("name").AsString());
    }

    [Fact]
    public async Task extract_throws_when_response_is_not_valid_JSON()
    {
        var (engine, provider) = NewFacade(out var facade);
        provider.RespondWith(new LlmResponse { Content = "not json" });

        var schema = await engine.EvaluateAsync("({type:'object'})", cancellationToken: TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => facade.extract("p", schema, null));
        Assert.Contains("non-JSON", ex.Message);
    }

    [Fact]
    public async Task decide_returns_one_of_choices()
    {
        var (engine, provider) = NewFacade(out var facade);
        provider.RespondWith(new LlmResponse { Content = "yes - we should" });

        var choices = await engine.EvaluateAsync("['yes', 'no']", cancellationToken: TestContext.Current.CancellationToken);
        var picked = await facade.decide("?", choices, null);

        Assert.Equal("yes", picked);
    }

    [Fact]
    public async Task decide_throws_when_response_is_not_among_choices()
    {
        var (engine, provider) = NewFacade(out var facade);
        provider.RespondWith(new LlmResponse { Content = "maybe" });

        var choices = await engine.EvaluateAsync("['yes', 'no']", cancellationToken: TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => facade.decide("?", choices, null));
        Assert.Contains("not one of", ex.Message);
    }

    [Fact]
    public async Task embed_returns_normalized_8d_vector_per_input()
    {
        var (engine, _) = NewFacade(out var facade);
        var input = await engine.EvaluateAsync("['hello', 'world']", cancellationToken: TestContext.Current.CancellationToken);

        var vectors = await facade.embed(input, null);

        // The CLR array Jint copies into a JS array on its event loop (ArrayConversionMode.Copy).
        var arr = Assert.IsType<double[][]>(vectors);
        Assert.Equal(2, arr.Length);
        Assert.All(arr, v => Assert.Equal(8, v.Length));
    }

    [Fact]
    public async Task act_returns_result_when_provider_does_not_request_a_tool_call()
    {
        var (_, provider) = NewFacade(out var facade);
        // act drives the conversation through ChatAsync; no tool_calls in the body → final answer.
        provider.RespondToChatWith(new LlmResponse { Content = "final answer" });

        var result = await facade.act("solve x", null);

        Assert.Equal("final answer", result.Get("output").AsString());
        Assert.Equal(1, Convert.ToInt32(result.Get("iterations").ToObject()));
    }

    [Fact]
    public async Task act_caps_iterations_at_maxIterations_when_provider_keeps_requesting_tools()
    {
        var (engine, provider) = NewFacade(out var facade);
        // Every turn returns an (OpenAI-shaped) tool call, so the loop never reaches a final answer.
        const string toolCallBody =
            "{\"choices\":[{\"message\":{\"content\":\"\",\"tool_calls\":[" +
            "{\"function\":{\"name\":\"foo\",\"arguments\":\"{}\"}}]}}]}";
        provider.RespondToChatWith(new LlmResponse { Content = "", RawResponseBody = toolCallBody });

        var opts = await engine.EvaluateAsync("({ maxIterations: 3 })", cancellationToken: TestContext.Current.CancellationToken);
        var result = await facade.act("p", opts);

        Assert.True(result.Get("exhausted").AsBoolean());
        Assert.Equal(3, Convert.ToInt32(result.Get("iterations").ToObject()));
    }
}
