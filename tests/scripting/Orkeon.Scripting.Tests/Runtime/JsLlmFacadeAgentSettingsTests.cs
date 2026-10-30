using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// #254: the <c>ctx.llm</c> calls of a procedural agent run with the effort and the prompt cache
/// the agent builder set, as a declarative agent of the same builder does; a call's own options win.
/// </summary>
public sealed class JsLlmFacadeAgentSettingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<StubLlmProvider> RunAsync(string builderCalls, string call)
    {
        var provider = new StubLlmProvider { BaseConfig = LlmConfig.Create("boot-model") };
        provider.RespondWith(new LlmResponse { Content = "ok" });
        var engine = new JsEngineFactory(builtInTools: [], llmProvider: provider).Create();
        var js = $$"""
            const a = agentBuilder().name("a").role("R").goal("G"){{builderCalls}}
                .body(async (input, ctx) => { return await ctx.llm.complete("hi"{{call}}); })
                .build();
            crewBuilder().name("c").withAgent(a).build();
            """;
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;
        await crew.RunAsync(null, Ct).WaitAsync(HangGuards.EnginePromise, Ct);
        return provider;
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldApplyTheAgentEffortAndCache_WhenTheCallSetsNoOptions()
    {
        var provider = await RunAsync(""".effort("high").cache({ systemPrompt: true })""", "");

        var config = Assert.IsType<LlmConfig>(provider.LastConfig);
        Assert.Equal("boot-model", config.Model);
        Assert.Equal("high", config.Thinking?.Effort);
        Assert.Equal(LlmCacheConfig.SystemPrompt(), config.Cache);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldApplyTheEffortOfTheAgentLlmConfig_WhenTheBuilderGotOneThroughLlm()
    {
        var provider = await RunAsync(""".llm(llm.default_.with({ effort: "low", cache: { tools: true } }))""", "");

        Assert.Equal("low", provider.LastConfig?.Thinking?.Effort);
        Assert.True(provider.LastConfig?.Cache?.CacheTools);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldLetTheCallEffortWin_WhenTheCallSetsOne()
    {
        var provider = await RunAsync(""".effort("high").cache({})""", """, { effort: "low" }""");

        Assert.Equal("low", provider.LastConfig?.Thinking?.Effort);
        Assert.Same(LlmCacheConfig.Off, provider.LastConfig?.Cache);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldLetTheCallCacheWin_WhenTheCallSetsOne()
    {
        var provider = await RunAsync(""".cache({ systemPrompt: true })""", """, { cache: { tools: true, ttl: "1h" } }""");

        var cache = Assert.IsType<LlmCacheConfig>(provider.LastConfig?.Cache);
        Assert.False(cache.CacheSystemPrompt);
        Assert.True(cache.CacheTools);
        Assert.Equal("1h", cache.Ttl);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldRefuseTheCallCache_WhenItIsNotAnObject()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync("", """, { cache: "1h" }"""));
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldHandOverNoConfig_WhenNeitherTheAgentNorTheCallSetsAnything()
    {
        var provider = await RunAsync("", "");

        Assert.Null(provider.LastConfig);
    }
}
