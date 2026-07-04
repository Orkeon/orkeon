using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Concurrency;

public sealed class LocksTests
{
    private static readonly string[] ExpectedAlternation = ["enter", "exit", "enter", "exit"];
    private static readonly string[] ExpectedSerializedOrder = ["A:in", "A:out", "B:in", "B:out"];

    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    /// <summary>Installs <c>__sleep(ms[, ct])</c> as a JS-awaitable Task helper.</summary>
    private static void InstallSleep(Engine engine)
    {
        engine.SetValue("__sleep", new Func<double, JsValue?, Task<JsValue>>(async (ms, ctVal) =>
        {
            var ct = CancellationToken.None;
            if (ctVal is not null && !ctVal.IsUndefined() && !ctVal.IsNull())
            {
                if (ctVal.ToObject() is CancellationToken token) ct = token;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(ms), ct).ConfigureAwait(false);
            return JsValue.Undefined;
        }));
    }

    [Fact]
    public void agentBuilder_concurrency_above_1_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                agentBuilder().name("A").role("R").goal("G").concurrency(2).build();
                """),
            "concurrency(1)");

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task ctx_lock_serializes_critical_section_within_an_agent()
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
    public async Task ctx_crew_lock_is_shared_across_agents_in_the_same_crew()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    await ctx.crew.lock("shared", async () => {
                        await ctx.memory.crew.store("trace",
                            (await ctx.memory.crew.get("trace") || "") + "A");
                    });
                    return await ctx.memory.crew.get("trace");
                }).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async (input, ctx) => {
                    await ctx.crew.lock("shared", async () => {
                        await ctx.memory.crew.store("trace",
                            (await ctx.memory.crew.get("trace") || "") + "B");
                    });
                    return await ctx.memory.crew.get("trace");
                }).build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(2, result.tasks.Count);
        Assert.Equal("AB", result.tasks[1].output!.ToString());
    }

    [Fact]
    public async Task ctx_lock_releases_when_callback_throws()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    try {
                        await ctx.lock("res", async () => { throw new Error("boom"); });
                    } catch (e) {}
                    await ctx.lock("res", async () => {});
                    return "ok";
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("ok", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task Concurrency_1_serializes_concurrent_calls_to_same_instance()
    {
        var engine = NewEngine();
        InstallSleep(engine);
        engine.SetValue("__counters", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G").concurrency(1)
                .body(async (input, ctx) => {
                    __counters.push("enter");
                    await __sleep(40);
                    __counters.push("exit");
                    return "ok";
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        await Task.WhenAll(
            crew.RunAsync(null, CancellationToken.None),
            crew.RunAsync(null, CancellationToken.None));

        var trace = (List<object>)engine.GetValue("__counters").ToObject()!;
        // Strict alternation enter/exit/enter/exit proves the mutex serialised the two
        // bodies — no overlap (no enter/enter or exit/exit pair).
        Assert.Equal(ExpectedAlternation, trace);
    }

    [Fact]
    public async Task state_with_concurrent_calls_serialize()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ n: 0 }))
                .body(async (input, ctx) => {
                    await Promise.all([
                        ctx.state.with(p => ({ n: p.n + 1 })),
                        ctx.state.with(p => ({ n: p.n + 1 })),
                        ctx.state.with(p => ({ n: p.n + 1 })),
                        ctx.state.with(p => ({ n: p.n + 1 })),
                        ctx.state.with(p => ({ n: p.n + 1 })),
                    ]);
                    return ctx.state.n;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(5d, Convert.ToDouble(result.tasks[0].output));
    }

    [Fact]
    public async Task state_with_callback_throwing_leaves_state_unchanged()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ n: 7 }))
                .body(async (input, ctx) => {
                    try {
                        await ctx.state.with(p => { throw new Error("boom"); });
                    } catch (e) {}
                    // Mutex must have been released — next `.with` should succeed.
                    await ctx.state.with(p => ({ n: p.n + 1 }));
                    return ctx.state.n;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(8d, Convert.ToDouble(result.tasks[0].output));
    }

    [Fact]
    public async Task ctx_crew_lock_blocks_concurrent_agents()
    {
        var engine = NewEngine();
        InstallSleep(engine);
        engine.SetValue("__order", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    await Promise.all([
                        ctx.crew.lock("X", async () => {
                            __order.push("A:in");
                            await __sleep(40);
                            __order.push("A:out");
                        }),
                        ctx.crew.lock("X", async () => {
                            __order.push("B:in");
                            __order.push("B:out");
                        }),
                    ]);
                    return __order.join(",");
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        await crew.RunAsync(null, CancellationToken.None);

        var trace = (List<object>)engine.GetValue("__order").ToObject()!;
        // B must wait for A to release — A:in then A:out then B:in then B:out.
        Assert.Equal(ExpectedSerializedOrder, trace);
    }

    [Fact]
    public async Task ctx_lock_signal_cancellation_releases_waiter()
    {
        var engine = NewEngine();
        InstallSleep(engine);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    await Promise.all([
                        ctx.lock("X", async () => { await __sleep(5000, ctx.signal); }),
                        ctx.lock("X", async () => { return "should-not-run"; }),
                    ]);
                    return "done";
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => crew.RunAsync(null, cts.Token));
    }

    [Fact]
    public async Task state_mutation_outside_with_throws_typed_exception()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ n: 0 }))
                .body((input, ctx) => {
                    ctx.state.n = 42;
                    return "should-not-return";
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => crew.RunAsync(null, CancellationToken.None));
        // After SCR-12 §1, the typed CLR exception bubbles up either directly or via the
        // exception chain (Jint may wrap it in JavaScriptException / PromiseRejected).
        var chain = ChainText(ex);
        Assert.Contains(nameof(StateMutationOutsideWithException), chain);
    }
}
