using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// #439 - a <c>.ork.ts</c> agent leaves its thinking level to System One like a YAML agent on <c>effort: auto</c>:
/// through <c>.effort("auto")</c> or an <c>LlmConfig</c> whose <c>effort</c> is <c>"auto"</c>; a single
/// <c>ctx.llm</c> call takes a level only.
/// </summary>
public sealed class AgentEffortAutoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsCrew Crew(string agentCalls) => (JsCrew)new JsEngineFactory().Create().Evaluate($$"""
        const coder = agentBuilder().name("coder").role("Coder").goal("Change code"){{agentCalls}}.build();
        const change = taskBuilder().name("change").agent(coder)
            .description("Rename x").expectedOutput("Done").build();
        crewBuilder().name("auto").goal("Pick a level").withAgent(coder).withTasks([change]).build();
        """).ToObject()!;

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData(""".effort("auto")""")]
    [InlineData(""".llm(llm.default_.with({ effort: "auto" }))""")]
    public void ShouldLeaveTheLevelToSystemOne_WhenTheAgentAsksForTheEffortAuto(string agentCalls)
    {
        // Act
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(agentCalls));

        // Assert
        Assert.Equal(LlmAutoEffort.Any, config.Agents.Single().LlmConfig!.Thinking!.AutoEffort);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheCallSendingNothing_WhenASingleCtxLlmCallAsksForTheEffortAuto()
    {
        // Arrange
        var provider = new StubLlmProvider { BaseConfig = LlmConfig.Create("boot-model") };
        provider.RespondWith(new LlmResponse { Content = "ok" });
        var engine = new JsEngineFactory(builtInTools: [], llmProvider: provider).Create();
        var js = """
            const a = agentBuilder().name("a").role("R").goal("G")
                .body(async (input, ctx) => {
                    try { await ctx.llm.complete("hi", { effort: "auto" }); return "sent"; }
                    catch (e) { return String(e.message); }
                })
                .build();
            crewBuilder().name("c").withAgent(a).build();
            """;
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;

        // Act
        var output = await crew.RunAsync(null, Ct).WaitAsync(HangGuards.EnginePromise, Ct);

        // Assert
        Assert.Null(provider.LastConfig);
        Assert.Contains("not for one call", output.output?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldSendTheProfilesLevelWithNoMarker_WhenAnAgentOnEffortAutoMakesACtxLlmCall()
    {
        // Arrange
        var provider = new StubLlmProvider
        {
            BaseConfig = LlmConfig.Create("boot-model") with { Thinking = new LlmThinkingConfig { Effort = "high", BudgetTokens = 4000 } },
        };
        provider.RespondWith(new LlmResponse { Content = "ok" });
        var engine = new JsEngineFactory(builtInTools: [], llmProvider: provider).Create();
        var js = """
            const a = agentBuilder().name("a").role("R").goal("G").effort("auto")
                .body(async (input, ctx) => { await ctx.llm.complete("hi"); return "sent"; })
                .build();
            crewBuilder().name("c").withAgent(a).build();
            """;
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;

        // Act
        await crew.RunAsync(null, Ct).WaitAsync(HangGuards.EnginePromise, Ct);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true, Effort = "high", BudgetTokens = 4000 }, provider.LastConfig?.Thinking);
    }
}
