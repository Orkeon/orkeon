using System.Text.Json;
using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// Issue #459 end to end: an ordinary schema written in a script, whose objects do not say
/// <c>additionalProperties</c>, reaches the Anthropic wire closed; a schema the provider cannot
/// rewrite is refused before any HTTP call, with a sentence that names the keyword and its path.
/// </summary>
public sealed class JsLlmAnthropicSchemaTests : IDisposable
{
    private const string Answer =
        """{"content":[{"type":"text","text":"{\"colour\":\"red\"}"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly CapturingHttpMessageHandler _handler = new(Answer);
    private readonly AnthropicLlmProvider _provider;

    public JsLlmAnthropicSchemaTests()
    {
        _provider = new AnthropicLlmProvider(
            LlmConfig.Create("claude-opus-5-5") with { ApiKey = "sk-test" }, new SingleHandlerHttpClientFactory(_handler));
    }

    public void Dispose()
    {
        _provider.Dispose();
        _handler.Dispose();
    }

    /// <summary>Runs <paramref name="body"/> as a procedural agent's body and returns what it left in <c>globalThis.out</c>.</summary>
    private async Task<string> RunBodyAsync(string body)
    {
        var engine = new JsEngineFactory(builtInTools: [], llmProvider: _provider).Create();
        var js = $$"""
            globalThis.out = "";
            const a = agentBuilder().name("a").role("R").goal("G")
                .body(async (input, ctx) => { {{body}} return "done"; })
                .build();
            crewBuilder().name("c").withAgent(a).build();
            """;
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;
        await crew.RunAsync(null, Ct).WaitAsync(HangGuards.EnginePromise, Ct);
        return (await engine.EvaluateAsync("globalThis.out", null, Ct)).AsString();
    }

    private JsonElement SentSchema()
    {
        using var doc = JsonDocument.Parse(Assert.Single(_handler.Bodies));
        return doc.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema").Clone();
    }

    [Fact]
    [Trait("BR", "CH-459")]
    public async Task ShouldAnswerWithAClosedSchema_WhenExtractTakesTheIssueSchema()
    {
        var shown = await RunBodyAsync("""
            const answer = await ctx.llm.extract("Name one colour.", {
              type: "object",
              properties: { colour: { type: "string" } },
              required: ["colour"],
            });
            globalThis.out = answer.colour;
            """);

        Assert.Equal("red", shown);
        Assert.False(SentSchema().GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    [Trait("BR", "CH-459")]
    public async Task ShouldRefuseOneOfBeforeTheCall_WithTheKeywordAndItsPath()
    {
        var shown = await RunBodyAsync("""
            try {
              await ctx.llm.extract("Pick.", {
                type: "object",
                properties: { pick: { oneOf: [{ type: "string" }, { type: "integer" }] } },
              });
              globalThis.out = "answered";
            } catch (e) {
              globalThis.out = String(e.message);
            }
            """);

        Assert.Empty(_handler.Bodies);
        // The bridge wraps a host failure in "One or more errors occurred. (...)" for every provider
        // error, outside this issue: the sentence the provider wrote is asserted whole inside it.
        Assert.Contains(
            "ctx.llm.extract: the provider failed: Anthropic cannot take the response schema 'extract': "
            + "`oneOf` at `#/properties/pick` is not supported; use `anyOf`.",
            shown,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-459")]
    public async Task ShouldSendAClosedSchema_WhenWithResponseSchemaDeclaresAnOpenOne()
    {
        var crew = (JsCrew)(await new JsEngineFactory().Create().EvaluateAsync("""
            const a = agentBuilder().name('a').role('R').goal('G')
                .withResponseSchema('colour', {
                    type: 'object',
                    properties: { colour: { type: 'string' } },
                    required: ['colour'],
                })
                .build();
            const t = taskBuilder().description('d').expectedOutput('json').agent(a).build();
            crewBuilder().name('c').withAgent(a).withTask(t).build();
            """, null, Ct)).ToObject()!;
        var format = Assert.Single(JsCrewConfigurationAdapter.ToConfiguration(crew).Agents).LlmConfig!.ResponseFormat!;

        var response = await _provider.ChatAsync(
            [LlmMessage.User("Name one colour.")], _provider.BaseConfig! with { ResponseFormat = format }, Ct);

        Assert.Null(response.Error);
        Assert.True(format.Schema!.Strict);
        Assert.DoesNotContain("additionalProperties", format.Schema.Schema, StringComparison.Ordinal);
        Assert.False(SentSchema().GetProperty("additionalProperties").GetBoolean());
    }
}
