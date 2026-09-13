using Jint;
using Jint.Runtime;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Builders;

public sealed class CrewBuilderTests
{
    private static readonly string[] ExpectedAgentNames = ["A", "B"];

    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(Engine engine, string js)
        => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public async Task Crew_with_two_agents_sequential_runs_each_body_in_order()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G").body(() => "alpha").build();
            const b = agentBuilder().name("B").role("R").goal("G").body(() => "beta").build();
            crewBuilder().name("c").process("sequential").withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(2, result.tasks.Count);
        Assert.Equal(ExpectedAgentNames, result.tasks.Select(t => t.name).ToArray());
        Assert.Equal("beta", result.output);
    }

    [Fact]
    public void CrewBuilder_hierarchical_without_manager_throws_InvalidScriptException()
    {
        var engine = NewEngine();

        var ex = ThrowsContaining<InvalidScriptException>(
            () => engine.Evaluate("""
                const a = agentBuilder().name("A").role("R").goal("G").build();
                crewBuilder().process("hierarchical").withAgent(a).build();
                """),
            "manager");

        Assert.NotNull(ex);
    }

    [Fact]
    public void CrewBuilder_unknown_process_throws_InvalidScriptException()
    {
        var engine = NewEngine();

        var ex = ThrowsContaining<InvalidScriptException>(
            () => engine.Evaluate("""crewBuilder().process("nonsense");"""),
            "Unknown process");

        Assert.NotNull(ex);
    }

    /// <summary>
    /// <c>crew.add</c> is a JavaScript throw (SCR-25 T7): the script sees an <c>Error</c> its
    /// <c>catch</c> can handle, and the typed exception rides on it for the CLR side.
    /// </summary>
    [Fact]
    public void Crew_add_agent_already_in_another_crew_throws_AgentAlreadyInCrewException()
    {
        var engine = NewEngine();

        var thrown = Assert.Throws<JavaScriptException>(() => engine.Evaluate("""
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const c1 = crewBuilder().name("c1").withAgent(a).build();
            const c2 = crewBuilder().name("c2").build();
            c2.add(a);
            """));
        var ex = Assert.IsType<AgentAlreadyInCrewException>(JsHostError.Unwrap(thrown.Error));
        Assert.Equal("c1", ex.CrewName);
    }

    /// <summary>Same bridge for <c>crew.remove</c>.</summary>
    [Fact]
    public void Crew_remove_agent_from_other_crew_throws_AgentNotInThisCrewException()
    {
        var engine = NewEngine();

        var thrown = Assert.Throws<JavaScriptException>(() => engine.Evaluate("""
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const c1 = crewBuilder().name("c1").withAgent(a).build();
            const c2 = crewBuilder().name("c2").build();
            c2.remove(a);
            """));
        var ex = Assert.IsType<AgentNotInThisCrewException>(JsHostError.Unwrap(thrown.Error));
        Assert.Equal("c2", ex.CrewName);
    }

    [Fact]
    public void Crew_remove_then_add_to_other_crew_succeeds()
    {
        var engine = NewEngine();

        engine.Evaluate("""
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const c1 = crewBuilder().name("c1").withAgent(a).build();
            c1.remove(a);
            const c2 = crewBuilder().name("c2").withAgent(a).build();
            globalThis.attached = c2.has(a);
            """);

        Assert.True(engine.Evaluate("globalThis.attached").AsBoolean());
    }

    [Fact]
    public void Crew_has_returns_true_after_add_false_after_remove()
    {
        var engine = NewEngine();
        engine.Evaluate("""
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const c = crewBuilder().build();
            c.add(a);
            globalThis.before = c.has(a);
            c.remove(a);
            globalThis.after = c.has(a);
            """);

        Assert.True(engine.Evaluate("globalThis.before").AsBoolean());
        Assert.False(engine.Evaluate("globalThis.after").AsBoolean());
    }

    [Fact]
    public void Crew_findByName_returns_agent_or_undefined()
    {
        var engine = NewEngine();
        engine.Evaluate("""
            const a = agentBuilder().name("Alice").role("R").goal("G").build();
            const c = crewBuilder().withAgent(a).build();
            globalThis.found = c.findByName("Alice");
            globalThis.missing = c.findByName("Bob");
            """);

        var found = Assert.IsType<JsAgent>(engine.Evaluate("globalThis.found").ToObject());
        Assert.Equal("Alice", found.name);
        Assert.True(engine.Evaluate("globalThis.missing == null").AsBoolean());
    }

    [Fact]
    public void Crew_duplicate_agent_name_throws_DuplicateAgentNameException()
    {
        var engine = NewEngine();

        var ex = Assert.Throws<DuplicateAgentNameException>(() => engine.Evaluate("""
            const a1 = agentBuilder().name("Twin").role("R").goal("G").build();
            const a2 = agentBuilder().name("Twin").role("R").goal("G").build();
            crewBuilder().withAgent(a1).withAgent(a2).build();
            """));
        Assert.Equal("Twin", ex.AgentName);
    }

    [Fact]
    public async Task Crew_run_with_already_cancelled_token_throws_OperationCanceledException()
    {
        var engine = NewEngine();
        engine.SetValue("__hooks", 0);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G").body(() => { __hooks += 100; return "x"; }).build();
            crewBuilder().withAgent(a)
                .onCrewStart((ctx) => { __hooks += 1; })
                .onCrewError((ctx, msg) => { __hooks += 10; })
                .build();
            """);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => crew.RunAsync(null, cts.Token));

        // A CLR caller with a cancelled token is refused before the loop starts: no hook, no body.
        Assert.Equal(0d, engine.GetValue("__hooks").AsNumber());
    }

    /// <summary>
    /// <c>crew.runStream</c> is a JS async generator (SCR-25 T4), consumed the way a script does —
    /// <c>for await</c> — one <c>agent.start</c>/<c>agent.stop</c> pair per agent, in declaration order.
    /// </summary>
    [Fact]
    public async Task Crew_runStream_yields_one_start_and_stop_per_agent()
    {
        var engine = NewEngine();
        var types = await engine.EvaluateAsync("""
            const a = agentBuilder().name("A").role("R").goal("G").body(() => "x").build();
            const b = agentBuilder().name("B").role("R").goal("G").body(() => "y").build();
            const crew = crewBuilder().withAgent(a).withAgent(b).build();
            (async () => { const t = []; for await (const e of crew.runStream()) t.push(e.type + ":" + e.payload.name); return t.join(","); })()
            """, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("agent.start:A,agent.stop:A,agent.start:B,agent.stop:B", types.AsString());
    }
}
