using System.Text.Json;
using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// #359: a script reads what the model of a configuration takes for reasoning. #360: it reads what the runtime
/// sends for the effort it asked, on the configuration and on the result of a call. Driven through Z.AI's
/// GLM-5.3, which takes <c>low</c>, <c>high</c> and <c>max</c> only.
/// </summary>
public sealed class JsLlmReasoningTests : IDisposable
{
    private const string Answer =
        """{"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"total_tokens":2,"prompt_tokens":1,"completion_tokens":1}}""";

    private static readonly string[] ActThenChat = ["high", "low"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly CapturingHttpMessageHandler _handler = new(Answer);
    private readonly ZaiLlmProvider _provider;

    public JsLlmReasoningTests()
    {
        _provider = new ZaiLlmProvider(
            LlmConfig.Create("glm-5.3") with { ApiKey = "sk-test" }, new SingleHandlerHttpClientFactory(_handler));
    }

    public void Dispose()
    {
        _provider.Dispose();
        _handler.Dispose();
    }

    private string Eval(string js) => new JsEngineFactory(llmProvider: _provider).Create().Evaluate(js).AsString();

    /// <summary>Runs <paramref name="body"/> as a procedural agent's body and returns what it left in <c>globalThis.out</c>.</summary>
    private async Task<string> RunBodyAsync(string builderCalls, string body)
    {
        var engine = new JsEngineFactory(builtInTools: [], llmProvider: _provider).Create();
        var js = $$"""
            globalThis.out = "";
            const a = agentBuilder().name("a").role("R").goal("G"){{builderCalls}}
                .body(async (input, ctx) => { {{body}} return "done"; })
                .build();
            crewBuilder().name("c").withAgent(a).build();
            """;
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;
        await crew.RunAsync(null, Ct).WaitAsync(HangGuards.EnginePromise, Ct);
        return (await engine.EvaluateAsync("globalThis.out", null, Ct)).AsString();
    }

    private static string Show(string value) =>
        $"[{value}.asked, {value}.sent, {value}.notice, {value}.model].join('|')";

    [Fact]
    [Trait("BR", "CH-359")]
    public void ShouldListTheLevelsOfTheModel_WhenTheScriptReadsTheCapabilities()
    {
        var shown = Eval("""
            const c = llm.default_.capabilities;
            [c.model, c.known, c.thinking, Array.isArray(c.efforts), c.efforts.join(',')].join('|')
            """);

        Assert.Equal("glm-5.3|true|effortOnly|true|low,high,max", shown);
    }

    [Fact]
    [Trait("BR", "CH-359")]
    public void ShouldSayTheModelIsUnknown_WhenTheTableHasNoEntryForIt()
    {
        var shown = Eval("""
            const c = llm.model("house-model-7").capabilities;
            [c.model, c.known, c.efforts.join(',')].join('|')
            """);

        Assert.Equal($"house-model-7|false|{string.Join(',', _provider.Capabilities.EffortLevels)}", shown);
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public void ShouldNameTheLevelSent_WhenTheConfigurationAsksForOneTheModelLowers()
    {
        Assert.Equal("medium|low|lowered|glm-5.3", Eval($"const r = llm.default_.with({{ effort: 'medium' }}).reasoning; {Show("r")}"));
        Assert.Equal("high|high||glm-5.3", Eval($"const r = llm.default_.with({{ effort: 'high' }}).reasoning; {Show("r")}"));
        Assert.Equal("null", Eval("String(llm.default_.reasoning)"));
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public async Task ShouldCarryTheEffortSent_OnTheResultOfActChatAndStream()
    {
        var shown = await RunBodyAsync(".effort(\"xhigh\")", $$"""
            const acted = await ctx.llm.act("hi");
            const chatted = await ctx.llm.chat([{ role: "user", content: "hi" }], { effort: "medium" });
            const streamed = ctx.llm.stream("hi", { effort: "max" });
            globalThis.out = [{{Show("acted.reasoning")}}, {{Show("chatted.reasoning")}}, {{Show("streamed.reasoning")}}].join(' / ');
            """);

        Assert.Equal("xhigh|high|lowered|glm-5.3 / medium|low|lowered|glm-5.3 / max|max||glm-5.3", shown);
        // What the result names is what went out.
        var sent = _handler.Bodies.Select(b => JsonElement.Parse(b).GetProperty("reasoning_effort").GetString());
        Assert.Equal(ActThenChat, sent.Take(2));
    }

    /// <summary>
    /// <c>complete</c> and <c>extract</c> answer a string or a value, which carries no report: inside an agent,
    /// <c>ctx.llm.reasoning(opts)</c> judges the call those options make, on the agent's provider, model and effort.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-360")]
    public async Task ShouldJudgeTheCallTheOptionsMake_WhenTheScriptAsksTheAgentsLlm()
    {
        var shown = await RunBodyAsync(".effort(\"medium\")", $$"""
            const before = ctx.llm.reasoning();
            await ctx.llm.complete("hi");
            const perCall = ctx.llm.reasoning({ effort: "max" });
            const unknown = ctx.llm.reasoning({ llm: { model: "house-model-7" }, effort: "high" });
            globalThis.out = [{{Show("before")}}, {{Show("perCall")}}, {{Show("unknown")}}].join(' / ');
            """);

        Assert.Equal("medium|low|lowered|glm-5.3 / max|max||glm-5.3 / high|high|unchecked|house-model-7", shown);
        // The report read before the call names what the call sent.
        var sent = JsonElement.Parse(Assert.Single(_handler.Bodies)).GetProperty("reasoning_effort").GetString();
        Assert.Equal("low", sent);
    }

    [Fact]
    [Trait("BR", "CH-359")]
    public async Task ShouldListTheLevelsOfTheCallsModel_WhenTheScriptAsksTheAgentsLlm()
    {
        var shown = await RunBodyAsync("", """
            const own = ctx.llm.capabilities();
            const other = ctx.llm.capabilities({ llm: { model: "house-model-7" } });
            globalThis.out = [own.model, own.known, own.efforts.join(','), other.model, other.known].join('|');
            """);

        Assert.Equal("glm-5.3|true|low,high,max|house-model-7|false", shown);
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public async Task ShouldCarryNoReasoning_WhenNoEffortIsAsked()
    {
        var shown = await RunBodyAsync("", """
            const acted = await ctx.llm.act("hi");
            const chatted = await ctx.llm.chat([{ role: "user", content: "hi" }]);
            globalThis.out = [acted.reasoning === null, chatted.reasoning === null, ctx.llm.stream("hi").reasoning === null].join('|');
            """);

        Assert.Equal("true|true|true", shown);
    }

    [Theory]
    [Trait("BR", "CH-364")]
    [InlineData("ctx.llm.reasoning({ llm: { profile: \"ghost\" } })")]
    [InlineData("ctx.llm.capabilities({ llm: { profile: \"ghost\" } })")]
    [InlineData("ctx.llm.stream(\"hi\", { llm: { profile: \"ghost\" } })")]
    public async Task ShouldReachTheBodysCatch_WhenTheCallNamesAProfileTheHostLacks(string call)
    {
        var shown = await RunBodyAsync("", $$"""
            let caught = "not caught";
            try { {{call}}; } catch (e) { caught = (e instanceof Error) + "|" + e.message; }
            globalThis.out = caught;
            """);

        Assert.StartsWith("true|", shown, StringComparison.Ordinal);
        Assert.Contains("names the LLM profile 'ghost', which this host does not offer. Known profiles: default.", shown, StringComparison.Ordinal);
        Assert.Empty(_handler.Bodies);
    }
}
