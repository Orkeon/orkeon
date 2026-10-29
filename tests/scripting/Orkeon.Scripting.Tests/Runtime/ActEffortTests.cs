using Orkeon.Domain.Tools;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-15: <c>act(prompt, {effort})</c> turns thinking on at that effort on the provider request.</summary>
public sealed class ActEffortTests
{
    private static JsCrew Eval(Jint.Engine engine, string js) => (JsCrew)engine.Evaluate(js).ToObject()!;

    [Fact]
    [Trait("BR", "RT-15")]
    public async Task ShouldSetThinkingEffortOnProviderRequest_WhenActEffortHigh()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{}");
        var engine = new JsEngineFactory(builtInTools: new IBaseTool[] { new StubBaseTool("file_read").RespondWithSuccess("c") }, llmProvider: provider).Create();
        var crew = Eval(engine, """
            const a = agentBuilder().name("reader").role("R").goal("G")
                .tools(["file_read"])
                .body(async (input, ctx) =>
                    (await ctx.llm.act("think", { effort: "high", permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().withAgent(a).build();
            """);

        await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(provider.ThinkingPerTurn);
        Assert.Equal((true, "high"), provider.ThinkingPerTurn[0]);
    }

    [Fact]
    [Trait("BR", "RT-15")]
    public async Task ShouldSetThinkingEffortOnProviderRequest_WhenActEffortXhigh()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{}");
        var engine = new JsEngineFactory(builtInTools: new IBaseTool[] { new StubBaseTool("file_read").RespondWithSuccess("c") }, llmProvider: provider).Create();
        var crew = Eval(engine, """
            const a = agentBuilder().name("reader").role("R").goal("G")
                .tools(["file_read"])
                .body(async (input, ctx) =>
                    (await ctx.llm.act("think", { effort: "xhigh", permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().withAgent(a).build();
            """);

        await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(provider.ThinkingPerTurn);
        Assert.Equal((true, "xhigh"), provider.ThinkingPerTurn[0]);
    }
}
