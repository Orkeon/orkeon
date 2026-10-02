using Jint;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.ErrorPolicy;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// GAP-27: what an agent writes in <c>.tools([...])</c> is what <c>ctx.llm.act</c> offers. A name
/// the host's catalogue does not answer to used to be dropped without a word — a typo
/// (<c>web_serch</c>) ran the loop with no tool at all — while the declarative shape refuses the
/// same name at load (<c>StrictTools</c>). <c>act</c> now rejects with the declared
/// <c>UnknownToolError</c>, naming the unknown names and the host's catalogue, before any model
/// call. The check runs at <c>act</c>, where the host's catalogue is known; names still match
/// case-insensitively.
/// </summary>
public sealed class ActUnknownToolTests
{
    private static Engine NewEngine(FakeToolCallingLlmProvider provider, params IBaseTool[] hostTools)
        => new JsEngineFactory(builtInTools: hostTools, llmProvider: provider).Create();

    private static JsCrew Eval(Engine engine, string js) => (JsCrew)engine.Evaluate(js).ToObject()!;

    [Fact]
    public async Task An_unknown_tool_name_rejects_act_with_an_UnknownToolError_naming_it()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{}");
        var engine = NewEngine(provider, new StubBaseTool("file_read"), new StubBaseTool("web_search"));
        var crew = Eval(engine, """
            const a = agentBuilder().name("researcher").role("R").goal("G")
                .tools(["file_read", "inconnu"])
                .body(async (input, ctx) => {
                    try { await ctx.llm.act("find it"); return "no throw"; }
                    catch (e) {
                        return [e instanceof UnknownToolError, e instanceof Error, e.name, e.clrType,
                            e.agentName, e.toolNames.join("+"), e.availableTools.join("+"), e.message].join("|");
                    }
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        var parts = result.tasks[0].output!.ToString()!.Split('|');
        Assert.Equal("true", parts[0]);
        Assert.Equal("true", parts[1]);
        Assert.Equal("UnknownToolError", parts[2]);
        Assert.Equal(nameof(UnknownToolException), parts[3]);
        Assert.Equal("researcher", parts[4]);
        Assert.Equal("inconnu", parts[5]);
        Assert.Equal("file_read+web_search", parts[6]);
        Assert.Contains("'inconnu'", parts[7], StringComparison.Ordinal);
        Assert.Contains("file_read, web_search", parts[7], StringComparison.Ordinal);
        // Refused before the loop: no model was asked anything.
        Assert.Empty(provider.Turns);
    }

    [Fact]
    public async Task A_known_name_in_another_case_is_still_offered()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{\"path\":\"/x\"}");
        var tool = new StubBaseTool("file_read").RespondWithSuccess("contents");
        var engine = NewEngine(provider, tool);
        var crew = Eval(engine, """
            const a = agentBuilder().name("reader").role("R").goal("G")
                .tools(["FILE_READ"])
                .body(async (input, ctx) =>
                    (await ctx.llm.act("read it", { permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(["file_read"], provider.OfferedTools);
        Assert.Single(tool.Calls);
        Assert.StartsWith("final: ", result.tasks[0].output!.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_on_a_host_without_a_tool_catalogue_is_unknown_too()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{}");
        var engine = NewEngine(provider);
        var crew = Eval(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").tools(["file_read"])
                .body(async (input, ctx) => {
                    try { await ctx.llm.act("x"); return "no throw"; }
                    catch (e) { return `${e instanceof UnknownToolError}|${e.toolNames.join("+")}|${e.availableTools.length}`; }
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("true|file_read|0", result.tasks[0].output!.ToString());
        Assert.Empty(provider.Turns);
    }

    [Fact]
    public async Task An_uncaught_unknown_tool_fails_the_run_with_the_typed_exception()
    {
        var provider = new FakeToolCallingLlmProvider("file_read", "{}");
        var engine = NewEngine(provider, new StubBaseTool("file_read"));
        var crew = Eval(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").tools(["web_serch"])
                .body(async (input, ctx) => (await ctx.llm.act("x")).output).build();
            crewBuilder().withAgent(a).build();
            """);

        var error = await Assert.ThrowsAsync<UnknownToolException>(
            () => crew.RunAsync(null, TestContext.Current.CancellationToken));

        Assert.Equal("a", error.AgentName);
        Assert.Equal(["web_serch"], error.ToolNames);
        Assert.Equal(["file_read"], error.AvailableTools);
    }

    [Fact]
    public void An_onError_handler_reads_it_as_a_validation_error()
    {
        Assert.Equal(
            ErrorCodeMapper.CodeValidation,
            ErrorCodeMapper.MapToCode(new UnknownToolException("a", ["web_serch"], ["web_search"])));
    }

    [Fact]
    public async Task A_body_that_never_calls_act_runs_whatever_its_tools_say()
    {
        // The catalogue is checked by act, the only reader of `.tools([...])` in a body.
        var provider = new FakeToolCallingLlmProvider("file_read", "{}");
        var engine = NewEngine(provider, new StubBaseTool("file_read"));
        var crew = Eval(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").tools(["inconnu"])
                .body(async (input) => "done").build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("done", result.tasks[0].output!.ToString());
    }
}
