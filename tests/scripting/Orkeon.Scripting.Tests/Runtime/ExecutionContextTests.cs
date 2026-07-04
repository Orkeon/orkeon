using Jint;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class ExecutionContextTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(Engine engine, string js)
        => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public async Task ExecutionContext_delegate_invokes_target_body_and_returns_result()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const out = await ctx.delegate(ctx.crew.findByName("B"), { x: 5 });
                    return `delegated:${out.doubled}`;
                }).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body((input, ctx) => input ? { doubled: input.x * 2 } : { doubled: 0 }).build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(2, result.tasks.Count);
        Assert.Equal("A", result.tasks[0].name);
        Assert.Equal("delegated:10", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task ExecutionContext_delegate_awaits_body_past_Jint_default_10s_ceiling()
    {
        // Regression: ctx.delegate used to await the delegated body via Jint's
        // UnwrapIfPromiseAsync, which bakes in a hard 10s settle ceiling. A
        // delegated agent doing real work (LLM call: 30-240s) was rejected with
        // "Timeout of 00:00:10 reached" while its body kept running detached —
        // the work happened but the return value was lost. This delegated body
        // awaits an 11s host operation; the fix must return its real result.
        var engine = NewEngine();
        engine.SetValue("hostSlow", new Func<double, Task<string>>(async ms =>
        {
            await Task.Delay((int)ms);
            return "slow-done";
        }));
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const out = await ctx.delegate(ctx.crew.findByName("B"), {});
                    return `parent:${out}`;
                }).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async () => await hostSlow(11000)).build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("parent:slow-done", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task ExecutionContext_concurrent_delegate_fanout_settles_under_single_outer_pump()
    {
        // Regression (R28): the commands fan-out fired ~50 ctx.delegate calls
        // under one Promise.all. delegate used to unwrap each body via a nested
        // blocking pump (Task.Run -> Jint UnwrapIfPromise) on the single-threaded,
        // non-reentrant engine, so N concurrent pumps deadlocked — every delegate
        // burned its full settle ceiling ("Timeout of 00:30:00 reached"). delegate
        // now returns the body's promise straight to JS, so N delegated bodies
        // settle on the one engine thread under the single outer crew pump. This
        // fans out to 8 delegates, each awaiting a host op; with the old code it
        // deadlocked, so merely completing with all 8 results proves the fix.
        var engine = NewEngine();
        engine.SetValue("hostSlow", new Func<double, string, Task<string>>(async (ms, tag) =>
        {
            await Task.Delay((int)ms);
            return tag;
        }));
        var crew = Eval<JsCrew>(engine, """
            let cb = crewBuilder();
            const coordinator = agentBuilder().name("Coord").role("R").goal("G")
                .body(async (input, ctx) => {
                    const names = [];
                    for (let i = 0; i < 8; i++) names.push(`W${i}`);
                    const results = await Promise.all(
                        names.map(n => ctx.delegate(ctx.crew.findByName(n), {})));
                    return results.join(",");
                }).build();
            cb = cb.withAgent(coordinator);
            for (let i = 0; i < 8; i++) {
                const w = agentBuilder().name(`W${i}`).role("R").goal("G")
                    .body(async () => await hostSlow(200, `done-${i}`)).build();
                cb = cb.withAgent(w);
            }
            cb.build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("done-0,done-1,done-2,done-3,done-4,done-5,done-6,done-7",
            result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task ExecutionContext_send_then_receive_passes_message()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.send(ctx.crew.findByName("B"), { greeting: "hi" });
                    return "sent";
                }).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async (input, ctx) => {
                    const msg = await ctx.receive({ timeout: 2000 });
                    return msg.greeting;
                }).build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("hi", result.tasks[1].output!.ToString());
    }

    [Fact]
    public async Task ExecutionContext_receive_throws_ReceiveTimeoutException_when_no_message_arrives()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => await ctx.receive({ timeout: 50 })).build();
            crewBuilder().withAgent(a).build();
            """);

        var ex = await ThrowsContainingAcrossChainAsync(
            () => crew.RunAsync(null, CancellationToken.None),
            nameof(ReceiveTimeoutException));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task ExecutionContext_memory_crew_is_shared_across_agents()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const writer = agentBuilder().name("W").role("R").goal("G")
                .body(async (input, ctx) => {
                    await ctx.memory.crew.store("answer", 42);
                    return "stored";
                }).build();
            const reader = agentBuilder().name("R").role("R").goal("G")
                .body(async (input, ctx) => await ctx.memory.crew.get("answer")).build();
            crewBuilder().withAgent(writer).withAgent(reader).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(42d, Convert.ToDouble(result.tasks[1].output));
    }

    [Fact]
    public async Task ExecutionContext_signal_propagates_cancellation_to_agent_body()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => await ctx.receive({ timeout: 5000 })).build();
            crewBuilder().withAgent(a).build();
            """);

        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAsync<OperationCanceledException>(() => crew.RunAsync(null, cts.Token));
    }

    [Fact]
    public async Task ExecutionContext_delegate_throws_AgentNotInCrewException_for_detached_self()
    {
        var engine = NewEngine();
        var detached = Eval<JsAgent>(engine, """
            agentBuilder().name("Loner").role("R").goal("G").body(() => "x").build();
            """);
        var target = Eval<JsAgent>(engine, """
            agentBuilder().name("T").role("R").goal("G").body(() => "y").build();
            """);
        var emptyCrew = Eval<JsCrew>(engine, """
            crewBuilder().name("empty").build();
            """);
        var ctx = new JsExecutionContext(
            engine, detached, emptyCrew,
            new JsAgentChannel(), new JsMemoryScope(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            CancellationToken.None);

        // delegate is now synchronous (returns the body's promise to JS rather
        // than unwrapping it in C#), so the detached-self guard throws on the
        // synchronous invocation, not as a faulted Task.
        Assert.Throws<AgentNotInCrewException>(() => ctx.@delegate(target, null!));
    }

    [Fact]
    public async Task ExecutionContext_broadcast_delivers_to_every_other_agent()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.broadcast({ note: "hello" });
                    return "broadcast";
                }).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async (input, ctx) => {
                    const msg = await ctx.receive({ timeout: 2000 });
                    return msg.note;
                }).build();
            const c = agentBuilder().name("C").role("R").goal("G")
                .body(async (input, ctx) => {
                    const msg = await ctx.receive({ timeout: 2000 });
                    return msg.note;
                }).build();
            crewBuilder().withAgent(a).withAgent(b).withAgent(c).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("hello", result.tasks[1].output!.ToString());
        Assert.Equal("hello", result.tasks[2].output!.ToString());
    }
}
