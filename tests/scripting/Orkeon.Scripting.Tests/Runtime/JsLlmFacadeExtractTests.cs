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
    public async Task Schema_goes_as_json_schema_when_the_provider_validates_one()
    {
        var (engine, provider) = NewFacade(out var facade, ResponseFormatSupport.JsonSchema);

        await facade.ExtractAsync("Parse this", Eval(engine, "({ type: 'object', properties: { name: { type: 'string' } } })"), null);

        var format = provider.LastConfig!.ResponseFormat!;
        Assert.Equal("json_schema", format.Type);
        Assert.Equal("extract", format.Schema!.Name);
        Assert.True(format.Schema.Strict);
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
}
