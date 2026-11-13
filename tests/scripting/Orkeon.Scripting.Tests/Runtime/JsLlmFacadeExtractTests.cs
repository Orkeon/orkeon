using Jint;
using Jint.Native;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// <c>ctx.llm.extract</c> beyond the prompt string (#285): the <c>system</c> option, the messages
/// form a re-ask needs, and the schema-constrained response format when the provider validates one.
/// </summary>
public sealed class JsLlmFacadeExtractTests
{
    private const string Json = "{\"name\":\"Ada\"}";

    private static (Engine, StubLlmProvider) NewFacade(out JsLlmFacade facade, ResponseFormatSupport support = ResponseFormatSupport.None)
    {
        var engine = new Engine(opt => { JsEngineFactory.ApplyInteropPolicy(opt); opt.Constraints.PromiseTimeout = HangGuards.EnginePromise; });
        var provider = new StubLlmProvider
        {
            Capabilities = new LlmProviderCapabilities { ResponseFormat = support },
        };
        provider.RespondWith(new LlmResponse { Content = Json });
        provider.RespondToChatWith(new LlmResponse { Content = Json });
        facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        return (engine, provider);
    }

    private static JsValue Eval(Engine engine, string script) => engine.Evaluate(script);

    [Fact]
    public async Task System_option_goes_first_as_a_system_message_and_the_prompt_is_sent_untouched()
    {
        var (engine, provider) = NewFacade(out var facade);
        var options = Eval(engine, "({ system: 'You extract invoices.' })");

        var result = await facade.ExtractAsync("Invoice #12, total 40", Eval(engine, "({type:'object'})"), options);

        Assert.Equal("Ada", result.Get("name").AsString());
        Assert.Empty(provider.GenerateCalls);
        var messages = Assert.Single(provider.ChatCalls);
        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.StartsWith("You extract invoices.", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("Respond with ONLY a valid JSON object", messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("Invoice #12, total 40", messages[1].Content);
    }

    [Fact]
    public async Task Messages_object_form_forwards_the_turns_in_order_with_the_assistant_turn()
    {
        var (engine, provider) = NewFacade(out var facade);
        var prompt = Eval(engine, """
            ({ messages: [
                { role: 'user', content: 'Extract the invoice' },
                { role: 'assistant', content: 'I cannot do that.' },
                { role: 'user', content: 'Try again, the total is on line 3' },
            ] })
            """);

        await facade.ExtractAsync(prompt, Eval(engine, "({type:'object'})"), null);

        var messages = Assert.Single(provider.ChatCalls);
        Assert.Equal(["user", "assistant", "user"], messages.Select(m => m.Role).ToArray());
        Assert.Equal("Extract the invoice", messages[0].Content);
        Assert.Equal("I cannot do that.", messages[1].Content);
        Assert.StartsWith("Try again, the total is on line 3", messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("Respond with ONLY a valid JSON object", messages[2].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bare_array_form_is_accepted()
    {
        var (engine, provider) = NewFacade(out var facade);
        var prompt = Eval(engine, "([{ role: 'user', content: 'Extract' }])");

        var result = await facade.ExtractAsync(prompt, Eval(engine, "({type:'object'})"), null);

        Assert.Equal("Ada", result.Get("name").AsString());
        var messages = Assert.Single(provider.ChatCalls);
        var only = Assert.Single(messages);
        Assert.Equal("user", only.Role);
        Assert.StartsWith("Extract", only.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Messages_form_without_a_user_turn_carries_the_instruction_on_a_leading_system_message()
    {
        var (engine, provider) = NewFacade(out var facade);
        var prompt = Eval(engine, "([{ role: 'assistant', content: 'Previous answer' }])");

        await facade.ExtractAsync(prompt, JsValue.Undefined, null);

        var messages = Assert.Single(provider.ChatCalls);
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("Respond with ONLY a valid JSON object", messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("Previous answer", messages[1].Content);
    }

    [Fact]
    public async Task Prompt_form_without_system_still_uses_generate_with_the_instruction_appended()
    {
        var (engine, provider) = NewFacade(out var facade);

        await facade.ExtractAsync("Parse this", Eval(engine, "({type:'object'})"), null);

        Assert.Empty(provider.ChatCalls);
        var prompt = Assert.Single(provider.GenerateCalls);
        Assert.StartsWith("Parse this", prompt, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"object\"", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_goes_as_a_non_strict_json_schema_when_the_provider_validates_one()
    {
        var (engine, provider) = NewFacade(out var facade, ResponseFormatSupport.JsonSchema);

        await facade.ExtractAsync("Parse this", Eval(engine, "({ type: 'object', properties: { name: { type: 'string' } } })"), null);

        var format = provider.LastConfig!.ResponseFormat!;
        Assert.Equal("json_schema", format.Type);
        Assert.Equal("extract", format.Schema!.Name);
        Assert.False(format.Schema.Strict);
        Assert.Contains("\"name\"", format.Schema.Schema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_falls_back_to_json_object_when_the_provider_accepts_no_schema()
    {
        var (engine, provider) = NewFacade(out var facade, ResponseFormatSupport.JsonObject);

        await facade.ExtractAsync("Parse this", Eval(engine, "({type:'object'})"), null);

        var format = provider.LastConfig!.ResponseFormat!;
        Assert.Equal("json_object", format.Type);
        Assert.Null(format.Schema);
    }

    [Fact]
    public async Task No_schema_object_means_json_object_even_on_a_schema_capable_provider()
    {
        var (_, provider) = NewFacade(out var facade, ResponseFormatSupport.JsonSchema);

        await facade.ExtractAsync("Parse this", JsValue.Undefined, null);

        Assert.Equal("json_object", provider.LastConfig!.ResponseFormat!.Type);
    }

    [Fact]
    public async Task Explicit_text_response_format_wins_over_the_schema()
    {
        var (engine, provider) = NewFacade(out var facade, ResponseFormatSupport.JsonSchema);

        await facade.ExtractAsync("Parse this", Eval(engine, "({type:'object'})"), Eval(engine, "({ responseFormat: 'text' })"));

        Assert.Null(provider.LastConfig!.ResponseFormat);
    }

    [Fact]
    public async Task Chat_accepts_the_messages_object_form()
    {
        var (engine, provider) = NewFacade(out var facade);

        await facade.ChatAsync(Eval(engine, "({ messages: [{ role: 'user', content: 'hi' }] })"), null);

        var messages = Assert.Single(provider.ChatCalls);
        var only = Assert.Single(messages);
        Assert.Equal("hi", only.Content);
    }

    private const string Http400 = "Z.AI API error: BadRequest - {\"error\":{\"code\":\"1210\",\"message\":\"please use low, high, or max\"}}";

    private static (Engine, JsLlmFacade) FacadeAnswering(LlmResponse response, int? maxTokens = null)
    {
        var engine = new Engine(opt => { JsEngineFactory.ApplyInteropPolicy(opt); opt.Constraints.PromiseTimeout = HangGuards.EnginePromise; });
        var provider = new StubLlmProvider
        {
            BaseConfig = maxTokens is { } max ? LlmConfig.Create("glm-5.3", "key") with { MaxTokens = max } : null,
        };
        provider.RespondWith(response);
        provider.RespondToChatWith(response);
        return (engine, new JsLlmFacade(engine, provider, CancellationToken.None));
    }

    private static Dictionary<string, object> Meta(params (string Key, object Value)[] entries)
        => entries.ToDictionary(e => e.Key, e => e.Value);

    [Fact]
    public async Task Extract_raises_the_providers_error_instead_of_an_empty_reply()
    {
        var (_, facade) = FacadeAnswering(new LlmResponse { Content = "", Metadata = Meta(("error", Http400)) });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => facade.ExtractAsync("Parse this", JsValue.Undefined, null));

        Assert.StartsWith("ctx.llm.extract: the provider failed: Z.AI API error: BadRequest", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1210", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_answers_with_the_providers_error_never_an_empty_content()
    {
        var (engine, facade) = FacadeAnswering(new LlmResponse { Content = "", Metadata = Meta(("error", Http400)) });

        var result = await facade.ChatAsync(Eval(engine, "[{ role: 'user', content: 'hi' }]"), null);

        var content = (string)result.GetType().GetProperty("content")!.GetValue(result)!;
        Assert.Equal($"<undefined-llm:{Http400}>", content);
    }

    [Fact]
    public async Task Extract_names_an_empty_reply_cut_by_the_output_budget_with_its_token_counts()
    {
        var (_, facade) = FacadeAnswering(new LlmResponse
        {
            Content = "",
            PromptTokens = 11000,
            CompletionTokens = 24576,
            Metadata = Meta(("finish_reason", "length"), ("reasoning_tokens", 24576)),
        }, maxTokens: 24576);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => facade.ExtractAsync("Parse this", JsValue.Undefined, null));

        Assert.Contains("the provider returned an empty reply", ex.Message, StringComparison.Ordinal);
        Assert.Contains("the output budget (MaxTokens = 24576) ran out, reasoning included", ex.Message, StringComparison.Ordinal);
        Assert.Contains("finish_reason: length", ex.Message, StringComparison.Ordinal);
        Assert.Contains("prompt_tokens: 11000", ex.Message, StringComparison.Ordinal);
        Assert.Contains("completion_tokens: 24576", ex.Message, StringComparison.Ordinal);
        Assert.Contains("reasoning_tokens: 24576", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_names_an_empty_reply_even_without_a_finish_reason()
    {
        var (_, facade) = FacadeAnswering(new LlmResponse { Content = "" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => facade.ExtractAsync("Parse this", JsValue.Undefined, null));

        Assert.Equal("ctx.llm.extract: the provider returned an empty reply", ex.Message);
    }

    [Fact]
    public async Task Extract_gives_the_json_error_position_and_the_end_of_a_cut_reply()
    {
        var reply = "{\"items\": [" + string.Join(",", Enumerable.Range(0, 60).Select(i => $"\"item-{i}\"")) + ", \"last-one";
        var (_, facade) = FacadeAnswering(new LlmResponse { Content = reply, Metadata = Meta(("finish_reason", "length")) });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => facade.ExtractAsync("Parse this", JsValue.Undefined, null));

        Assert.Contains("non-JSON content (cut short: the output budget ran out)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("JSON error at line 1, byte ", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"Reply ({reply.Length} chars) starts: {{\"items\"", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith("ends: ..." + reply[^200..], ex.Message, StringComparison.Ordinal);
        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException, exactMatch: false);
    }
}
