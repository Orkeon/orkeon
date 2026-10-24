using Jint;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-19: <c>act(prompt, {tools: [...]})</c> offers only the named subset of the agent's tools.</summary>
public sealed class ActToolSubsetTests
{
    private static JsCrew Eval(Engine engine, string js) => (JsCrew)engine.Evaluate(js).ToObject()!;

    private static Engine NewEngine(FakeToolCallingLlmProvider provider, params IBaseTool[] hostTools)
        => new JsEngineFactory(builtInTools: hostTools, llmProvider: provider).Create();

    [Fact]
    [Trait("BR", "RT-19")]
    public async Task ShouldOfferOnlySubset_WhenToolsOptionGiven()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{\"path\":\"/x\"}");
        var engine = NewEngine(provider, new StubBaseTool("file_read").RespondWithSuccess("contents"), new StubBaseTool("web_search"));
        var crew = Eval(engine, """
            const a = agentBuilder().name("reader").role("R").goal("G")
                .tools(["file_read", "web_search"])
                .body(async (input, ctx) =>
                    (await ctx.llm.act("read it", { tools: ["file_read"], permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().withAgent(a).build();
            """);

        await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(provider.OfferedToolsPerTurn);
        Assert.Equal(["file_read"], provider.OfferedToolsPerTurn[0]);
    }

    [Fact]
    [Trait("BR", "RT-19")]
    public async Task ShouldThrowUnknownToolBeforeModelCall_WhenNameNotInAgentTools()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{}");
        var engine = NewEngine(provider, new StubBaseTool("file_read"), new StubBaseTool("web_search"));
        var crew = Eval(engine, """
            const a = agentBuilder().name("reader").role("R").goal("G")
                .tools(["file_read"])
                .body(async (input, ctx) => {
                    try { await ctx.llm.act("x", { tools: ["nope"] }); return "no throw"; }
                    catch (e) { return [e instanceof UnknownToolError, e.name, e.clrType, e.toolNames.join("+")].join("|"); }
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("true|UnknownToolError|" + nameof(UnknownToolException) + "|nope", result.tasks[0].output!.ToString());
        Assert.Empty(provider.Turns);
    }

    [Fact]
    [Trait("BR", "RT-19")]
    public async Task ShouldOfferSubset_WhenToolsNameDiffersInCase()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{\"path\":\"/x\"}");
        var engine = NewEngine(provider, new StubBaseTool("file_read").RespondWithSuccess("contents"), new StubBaseTool("web_search"));
        var crew = Eval(engine, """
            const a = agentBuilder().name("reader").role("R").goal("G")
                .tools(["file_read", "web_search"])
                .body(async (input, ctx) =>
                    (await ctx.llm.act("read it", { tools: ["File_Read"], permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().withAgent(a).build();
            """);

        await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(provider.OfferedToolsPerTurn);
        Assert.Equal(["file_read"], provider.OfferedToolsPerTurn[0]);
    }
}
