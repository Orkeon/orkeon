using Jint;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class AgentContextTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(Engine engine, string js)
        => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public async Task AgentContext_state_read_returns_initial_value_from_factory()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 7 }))
                .body((input, ctx) => ctx.state.counter).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(7d, Convert.ToDouble(result.tasks[0].output));
    }

    [Fact]
    public async Task AgentContext_state_with_replaces_atomically()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 0 }))
                .body(async (input, ctx) => {
                    await ctx.state.with(prev => ({ counter: prev.counter + 5 }));
                    await ctx.state.with(prev => ({ counter: prev.counter + 3 }));
                    return ctx.state.counter;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(8d, Convert.ToDouble(result.tasks[0].output));
    }

    [Fact]
    public async Task AgentContext_direct_state_mutation_throws_via_proxy_set_trap()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 0 }))
                .body((input, ctx) => {
                    ctx.state.counter = 99;
                    return ctx.state.counter;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        // The set trap throws through the host error bridge (a JS throw); the CLR side
        // recovers the typed exception from a synchronous body as well as from a rejection.
        var thrown = await Assert.ThrowsAsync<StateMutationOutsideWithException>(
            () => crew.RunAsync(null, CancellationToken.None));
        Assert.Equal("counter", thrown.PropertyName);
        Assert.Contains("StateMutationOutsideWith", thrown.ToString());
    }

    [Fact]
    public async Task AgentContext_direct_state_mutation_in_an_async_body_rejects_with_the_typed_exception()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 0 }))
                .body(async (input, ctx) => {
                    await Promise.resolve();
                    ctx.state.counter = 99;
                    return ctx.state.counter;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var thrown = await Assert.ThrowsAsync<StateMutationOutsideWithException>(
            () => crew.RunAsync(null, CancellationToken.None));
        Assert.Equal("counter", thrown.PropertyName);
    }

    /// <summary>
    /// A CLR exception that is not bridged runs neither <c>catch</c> nor <c>finally</c> in the
    /// script (measured, see <c>JsHostError</c>); the set trap is bridged, so a body can recover
    /// from its own mistake and the mutex-guarded path still works afterwards.
    /// </summary>
    [Fact]
    public async Task AgentContext_direct_state_mutation_is_catchable_by_the_body_and_runs_its_finally()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 1 }))
                .body(async (input, ctx) => {
                    const log = [];
                    try { ctx.state.counter = 99; log.push("unreachable"); }
                    catch (e) { log.push("caught:" + e.clrType + ":" + (e instanceof Error)); }
                    finally { log.push("finally"); }
                    await ctx.state.with(prev => ({ counter: prev.counter + 1 }));
                    return log.join(",") + "|" + ctx.state.counter;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("caught:StateMutationOutsideWithException:true,finally|2", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task AgentContext_state_with_resolves_to_the_new_state_view_carrying_with()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 0 }))
                .body(async (input, ctx) => {
                    const before = ctx.state;
                    const next = await ctx.state.with(prev => ({ counter: prev.counter + 5 }));
                    const again = await next.with(prev => ({ counter: prev.counter + 1 }));
                    return [next.counter, again.counter, ctx.state.counter, before.counter,
                            next === ctx.state, again === ctx.state, typeof ctx.stateWith].join(",");
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        // The view is replaced after each commit (the old one still reads its own snapshot),
        // the resolved value is the new view, and the mutator is also reachable as ctx.stateWith.
        Assert.Equal("5,6,6,0,false,true,function", result.tasks[0].output!.ToString());
    }

    /// <summary>
    /// The transform receives the raw state, not the read-only view: it may build the next
    /// state in place (chapter 05's <c>state.count += 1</c>) without tripping the set trap.
    /// </summary>
    [Fact]
    public async Task AgentContext_state_with_hands_the_transform_the_raw_state()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 0 }))
                .body(async (input, ctx) => {
                    await ctx.state.with(s => { s.counter += 1; return s; });
                    await ctx.stateWith(async s => { await Promise.resolve(); s.counter += 1; return s; });
                    return ctx.state.counter;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(2d, Convert.ToDouble(result.tasks[0].output));
    }

    /// <summary>
    /// A state the view cannot wrap (a primitive) fails at commit as a JS throw the script
    /// can catch; the state is untouched and the mutex is released.
    /// </summary>
    [Fact]
    public async Task AgentContext_state_with_returning_a_primitive_rejects_and_leaves_state_and_mutex_intact()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ counter: 7 }))
                .body(async (input, ctx) => {
                    let caught = "none";
                    try { await ctx.state.with(() => 5); }
                    catch (e) { caught = e instanceof TypeError ? "TypeError" : String(e); }
                    const after = ctx.state.counter;
                    await ctx.state.with(prev => ({ counter: prev.counter + 1 }));
                    return caught + "," + after + "," + ctx.state.counter;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("TypeError,7,8", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task AgentContext_state_with_undefined_state_is_reached_through_stateWith()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const before = typeof ctx.state;
                    const next = await ctx.stateWith(prev => ({ seeded: prev === undefined }));
                    return before + "," + next.seeded + "," + ctx.state.seeded + "," + typeof ctx.state.with;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        // No state → no view (undefined comes back as is); the first commit installs one.
        Assert.Equal("undefined,true,true,function", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task AgentContext_memory_agent_is_isolated_from_memory_crew()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    await ctx.memory.agent.store("k", "agent-only");
                    await ctx.memory.crew.store("k", "shared");
                    const a = await ctx.memory.agent.get("k");
                    const c = await ctx.memory.crew.get("k");
                    return `${a}|${c}`;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("agent-only|shared", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task AgentContext_lock_serializes_critical_section()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ trace: "" }))
                .body(async (input, ctx) => {
                    await ctx.lock("section", async () => {
                        await ctx.state.with(p => ({ trace: p.trace + "1" }));
                    });
                    await ctx.lock("section", async () => {
                        await ctx.state.with(p => ({ trace: p.trace + "2" }));
                    });
                    return ctx.state.trace;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("12", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task AgentContext_spawn_attaches_new_agent_to_current_crew()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => {
                    const child = ctx.spawn(
                        agentBuilder().name("Child").role("R").goal("G")
                            .body(() => "child-output"));
                    return child.name;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("Child", result.tasks[0].output!.ToString());
        Assert.True(crew.has(crew.findByName("Child")!));
    }

    [Fact]
    public async Task AgentContext_spawn_throws_RecursiveAgentInvocationException_when_name_collides_with_self()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => {
                    return ctx.spawn(
                        agentBuilder().name("A").role("R").goal("G").body(() => null));
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => crew.RunAsync(null, CancellationToken.None));
        Assert.Contains(nameof(RecursiveAgentInvocationException), thrown.ToString());
    }
}
