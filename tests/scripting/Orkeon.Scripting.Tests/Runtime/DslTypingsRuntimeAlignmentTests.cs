using Jint;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// GAP-12: the runtime half of the typings/runtime alignment. Each test is a shape the typings
/// declared and the runtime refused, ignored or did differently.
/// </summary>
public sealed class DslTypingsRuntimeAlignmentTests
{
    private static Engine NewEngine(ILoggerFactory? loggerFactory = null, Orkeon.Domain.SharedKernel.ILlmProvider? provider = null)
        => new JsEngineFactory(loggerFactory: loggerFactory, llmProvider: provider).Create();

    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    // ---- 5 / 7: an agent by name ---------------------------------------------------------

    [Fact]
    public async Task Send_and_delegate_accept_an_agent_name()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.send("writer", { greeting: "hi" });
                    const doubled = await ctx.delegate("helper", 21);
                    return `sent:${doubled}`;
                }).build();
            const helper = agentBuilder().name("helper").role("R").goal("G")
                .body(async (input) => input * 2).build();
            const writer = agentBuilder().name("writer").role("R").goal("G")
                .body(async (input, ctx) => (await ctx.receive({ timeout: 2000 })).greeting).build();
            crewBuilder().withAgent(a).withAgent(helper).withAgent(writer).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("sent:42", result.tasks[0].output!.ToString());
        Assert.Equal("hi", result.tasks[2].output!.ToString());
    }

    [Theory]
    [InlineData("ctx.send(\"ghost\", 1)")]
    [InlineData("await ctx.delegate(\"ghost\", 1)")]
    public async Task Send_and_delegate_to_an_unknown_name_throw_AgentNotInThisCrew(string call)
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, $$"""
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    try { {{call}}; return "no throw"; }
                    catch (e) { return `${e.clrType}|${e instanceof AgentNotInThisCrewError}|${e.agentName}`; }
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("AgentNotInThisCrewException|true|ghost", result.tasks[0].output!.ToString());
    }

    [Fact]
    public void Crew_has_and_remove_accept_an_agent_name()
    {
        var engine = NewEngine();
        var outcome = engine.Evaluate("""
            const w = agentBuilder().name("writer").role("R").goal("G").build();
            const crew = crewBuilder().withAgent(w).build();
            const before = crew.has("writer");
            const unknown = crew.has("ghost");
            crew.remove("writer");
            let removeUnknown;
            try { crew.remove("ghost"); removeUnknown = "no throw"; }
            catch (e) { removeUnknown = e instanceof AgentNotInThisCrewError; }
            [before, unknown, crew.has("writer"), crew.has(w), removeUnknown].join(",");
            """).AsString();

        Assert.Equal("true,false,false,false,true", outcome);
    }

    // ---- 6: a delegated body gets its own context ----------------------------------------

    [Fact]
    public async Task Delegated_body_reads_its_own_ctx()
    {
        using var lf = new RecordingLoggerFactory();
        var engine = NewEngine(lf);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => `got:${await ctx.delegate("B", 4)}`).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .withState(() => ({ n: 1 }))
                .body(async (input, ctx) => {
                    ctx.log.info("B runs");
                    await ctx.state.with(s => ({ n: s.n + input }));
                    return ctx.state.n;
                }).build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("got:5", result.tasks[0].output!.ToString());
        Assert.True(lf.Logger.HasEntry(e => e.Message == "B runs"));
    }

    [Fact]
    public async Task Delegated_body_goes_through_its_onError_policy()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => `got:${await ctx.delegate("B", null)}`).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async () => { throw new Error("boom"); })
                .onError(() => ErrorAction.fallback("recovered")).build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("got:recovered", result.tasks[0].output!.ToString());
    }

    // ---- 8: withAgent / withTask take built values ---------------------------------------

    [Theory]
    [InlineData("crewBuilder().withTask(b => b)")]
    [InlineData("crewBuilder().withTask({ description: 'x' })")]
    [InlineData("crewBuilder().withTasks([b => b])")]
    [InlineData("crewBuilder().withAgent(b => b)")]
    [InlineData("crewBuilder().withAgents([{ name: 'x' }])")]
    public void Crew_builder_refuses_what_is_not_a_built_agent_or_task(string js)
    {
        var engine = NewEngine();

        var ex = Assert.ThrowsAny<Exception>(() => engine.Evaluate(js));

        Assert.IsType<InvalidScriptException>(Innermost(ex));
    }

    // ---- 9: agent.role -------------------------------------------------------------------

    [Fact]
    public void Agent_exposes_its_role()
    {
        var engine = NewEngine();

        var role = engine.Evaluate("""agentBuilder().name("A").role("Researcher").goal("G").build().role""").AsString();

        Assert.Equal("Researcher", role);
    }

    // ---- 13 / 14: one honest llm surface -------------------------------------------------

    [Theory]
    [InlineData("agentBuilder().llm(\"gpt-4o\")")]
    [InlineData("agentBuilder().llm({ provider: \"deepseek\", model: \"deepseek-flash\" })")]
    public void Agent_llm_refuses_anything_but_an_LlmConfig(string js)
    {
        var engine = NewEngine();

        var ex = Assert.ThrowsAny<Exception>(() => engine.Evaluate(js));

        var inner = Assert.IsType<InvalidScriptException>(Innermost(ex));
        Assert.Contains("llm.default_.with({ model", inner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_llm_namespace_has_no_per_vendor_factory()
    {
        var engine = NewEngine();

        var kinds = engine.Evaluate("""
            ["openai", "anthropic", "ollama", "azureOpenai", "grok", "minimax", "openrouter", "mammouth", "default"]
                .map(k => typeof llm[k]).join(",");
            """).AsString();

        Assert.Equal(string.Join(",", Enumerable.Repeat("undefined", 9)), kinds);
    }

    [Fact]
    public void Llm_default_is_the_host_provider_on_its_own_model()
    {
        var engine = NewEngine(provider: new SlowLlmProvider());

        var def = Eval<JsLlmConfig>(engine, "llm.default_");

        Assert.Equal("slow", def.provider);
        Assert.Equal("slow", def.model);
    }

    [Fact]
    public void Llm_model_sets_the_model_of_the_host_provider()
    {
        var engine = NewEngine(provider: new SlowLlmProvider());

        var cfg = Eval<JsLlmConfig>(engine, """llm.model("deepseek-flash", { temperature: 0.1 })""");

        Assert.Equal("slow", cfg.provider);
        Assert.Equal("deepseek-flash", cfg.model);
        Assert.Equal(0.1, cfg.temperature);
    }

    // ---- 16: withTaskTool is gone --------------------------------------------------------

    [Fact]
    public void Task_builder_has_no_withTaskTool()
    {
        var engine = NewEngine();

        Assert.Equal("undefined", engine.Evaluate("typeof taskBuilder().withTaskTool").AsString());
    }

    // ---- 19: ctx.log formats its extra arguments -----------------------------------------

    [Fact]
    public async Task Log_writes_every_argument()
    {
        using var lf = new RecordingLoggerFactory();
        var engine = NewEngine(lf);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => { ctx.log.info("a", 1, { b: 2 }); return "ok"; }).build();
            crewBuilder().withAgent(a).build();
            """);

        await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.True(lf.Logger.HasEntry(e => e.Level == LogLevel.Information && e.Message == "a 1 {\"b\":2}"),
            string.Join("\n", lf.Logger.Entries.Select(e => e.Message)));
    }

    // ---- 1: the error classes are planted ------------------------------------------------

    [Fact]
    public async Task A_receive_timeout_is_an_instance_of_ReceiveTimeoutError()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const q = ctx.events.queue("q");
                    const seen = [];
                    try { await q.pop({ timeout: 1 }); } catch (e) {
                        seen.push(e instanceof ReceiveTimeoutError, e instanceof Error, e.timeoutMs, e.clrType);
                    }
                    try { await ctx.receive({ timeout: 1 }); } catch (e) {
                        seen.push(e instanceof ReceiveTimeoutError);
                    }
                    return seen.join(",");
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("true,true,1,ReceiveTimeoutException,true", result.tasks[0].output!.ToString());
    }

    [Fact]
    public void Every_declared_error_class_is_a_global_Error_subclass()
    {
        var engine = NewEngine();

        var names = engine.Evaluate("""
            ["ScriptVersionMismatchError", "AgentAlreadyInCrewError", "AgentNotInCrewError",
             "AgentNotInThisCrewError", "DuplicateAgentNameError", "RecursiveAgentInvocationError",
             "WaiterKickedError", "ReceiveTimeoutError", "StateMutationOutsideWithError",
             "BudgetExhaustedError", "UnknownToolError"]
                .filter(n => !(typeof globalThis[n] === "function" && new globalThis[n]("m") instanceof Error
                    && new globalThis[n]("m").name === n))
                .join(",");
            """).AsString();

        Assert.Equal("", names);
    }

    [Fact]
    public void A_duplicate_agent_is_an_instance_of_DuplicateAgentNameError()
    {
        var engine = NewEngine();

        var outcome = engine.Evaluate("""
            const crew = crewBuilder().withAgent(agentBuilder().name("A").role("R").goal("G").build()).build();
            let r;
            try { crew.add(agentBuilder().name("A").role("R").goal("G").build()); r = "no throw"; }
            catch (e) { r = `${e instanceof DuplicateAgentNameError}|${e.agentName}|${e.crewName}`; }
            r;
            """).AsString();

        Assert.Equal("true|A|crew", outcome);
    }

    // ---- 15: act offers the agent's tool instances ---------------------------------------

    [Fact]
    public async Task Act_runs_the_agent_s_autonomous_tool_instance_and_feeds_its_result_back()
    {
        var provider = new FakeToolCallingLlmProvider("word_count", "{\"text\":\"one two three\"}");
        var engine = NewEngine(provider: provider);
        var crew = Eval<JsCrew>(engine, """
            const wordCount = toolBuilder().name("word_count").description("Counts words")
                .withSchema({ type: "object", properties: { text: { type: "string" } }, required: ["text"] })
                .execute(async (input) => { await Promise.resolve(); return { words: input.text.split(" ").length }; })
                .build();
            const a = agentBuilder().name("A").role("R").goal("G")
                .withAutonomousTool(wordCount)
                .body(async (input, ctx) => (await ctx.llm.act("count the words", { permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Contains("word_count", provider.OfferedTools);
        var output = result.tasks[0].output!.ToString()!;
        Assert.StartsWith("final: ", output, StringComparison.Ordinal);
        Assert.Contains("[tool:word_count]", output, StringComparison.Ordinal);
        Assert.Contains("\"words\":3", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Act_feeds_a_throwing_tool_instance_back_as_an_error()
    {
        var provider = new FakeToolCallingLlmProvider("broken", "{}");
        var engine = NewEngine(provider: provider);
        var crew = Eval<JsCrew>(engine, """
            const broken = toolBuilder().name("broken").description("Throws")
                .withSchema({ type: "object", properties: {} })
                .execute(() => { throw new Error("tool exploded"); })
                .build();
            const a = agentBuilder().name("A").role("R").goal("G")
                .withAutonomousTool(broken)
                .body(async (input, ctx) => (await ctx.llm.act("go", { permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        var output = result.tasks[0].output!.ToString()!;
        Assert.Contains("ERROR", output, StringComparison.Ordinal);
        Assert.Contains("tool exploded", output, StringComparison.Ordinal);
    }

    private static Exception Innermost(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; )
        {
            if (cur is JavaScriptException js && Orkeon.Scripting.Internal.JsHostError.Unwrap(js.Error) is { } clr)
                cur = clr;
            else if (cur.InnerException is null)
                return cur;
            else
                cur = cur.InnerException;
            if (cur.InnerException is null && cur is not JavaScriptException)
                return cur;
        }
        return ex;
    }
}
