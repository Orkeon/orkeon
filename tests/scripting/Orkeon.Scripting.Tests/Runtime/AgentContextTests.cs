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

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => crew.RunAsync(null, CancellationToken.None));
        // After SCR-12 §1 the CLR-typed exception surfaces directly through the unwrap.
        Assert.Contains("StateMutationOutsideWith", thrown.ToString());
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
