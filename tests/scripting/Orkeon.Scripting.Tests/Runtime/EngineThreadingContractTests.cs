using System.Runtime.CompilerServices;
using Jint;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Tests.Runtime;

// xUnit1004 suppressed: the skips are intentional. Each skipped scenario is a reproducer of
// the engine-threading defect family documented in SCR-25, kept executable so the chantier
// that fixes it has its acceptance tests already written. Tracked: SCR-25.
#pragma warning disable xUnit1004 // Test methods should not be skipped

/// <summary>
/// The contract a script can rely on when its awaits really suspend: every scenario runs
/// through <see cref="ScriptHost"/> against a provider that yields to the thread pool
/// (<see cref="SlowProvider"/>), the way every HTTP provider does. The echo provider and
/// the stubs elsewhere in this suite complete synchronously, which is why none of this
/// surfaced there.
/// </summary>
/// <remarks>
/// <para>What the skipped scenarios have in common (SCR-25): a CLR continuation calls back
/// into the engine — an agent body, a lifecycle hook, a crew hook — from a thread that is not
/// the one draining the event loop, or it drains synchronously from inside a job. Jint's loop
/// is exclusive per drain but has no guard on <c>Engine.Invoke</c>, and a drain nested inside a
/// job cannot pump ("Nested inside a job it cannot pump", Jint <c>Engine.DrainEventLoopUntil</c>).
/// A synchronous drain (<c>JsCrew.UnwrapPromise</c>) reached from inside a job never settles.
/// The graph, FSM, state, topic and <c>act</c> scenarios are live: those surfaces are JS
/// trampolines since SCR-25 T1, T2, T3, T5 and T6.</para>
/// <para>One scenario (<c>runStream</c> on a crew) is not a race: it pins the shape the rewrite
/// must produce — a JS async generator, since Jint does not expose an <c>IAsyncEnumerable</c>
/// as an async iterable — the shape the graph's <c>runStream</c> already has.</para>
/// <para>A 20 s guard turns the runtime's 30-minute body timeout into a failure.</para>
/// </remarks>
public sealed class EngineThreadingContractTests
{
    private const string Scr25 = "SCR-25: reproducer of the engine-threading defect family; passes once CLR->JS callbacks go through JS trampolines";

    private sealed class SlowProvider : ILlmProvider
    {
        public string Name => "slow";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "slow" };

        public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
        {
            await Task.Delay(3, ct).ConfigureAwait(false);
            return new LlmResponse { Content = "R:" + prompt };
        }

        public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            await Task.Delay(3, ct).ConfigureAwait(false);
            return new LlmResponse { Content = "R:" + messages[^1].Content };
        }
    }

    /// <summary>
    /// A provider whose chat stream yields each delta after a real suspension, so the
    /// <c>onDelta</c> callback of <c>ctx.llm.act</c> is reached from a thread-pool continuation.
    /// </summary>
    private sealed class SlowStreamingProvider : ILlmProvider, IStreamingLlmProvider
    {
        public string Name => "slow-streaming";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "slow" };
        public bool SupportsStreaming => true;

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "R:" + prompt });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "R:" + messages[^1].Content });

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(3, ct).ConfigureAwait(false);
            yield return "R:" + prompt;
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < 3; i++)
            {
                await Task.Delay(3, ct).ConfigureAwait(false);
                yield return LlmStreamEvent.Content("d" + i);
            }
            yield return LlmStreamEvent.Complete(new LlmResponse { Content = "d0d1d2" });
        }
    }

    private static async Task<IDictionary<string, object?>> RunScriptAsync(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "orkeon-threading-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "probe.ork.ts"), source);
            var fs = new DiskBackedFileSystemService(dir, "/scripts");
            var host = new ScriptHost(fs, new JsEngineFactory(llmProvider: new SlowProvider()));

            object? raw;
            try
            {
                raw = await host.RunAsync("/scripts/probe.ork.ts", CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                Assert.Fail("HANG: the script did not settle within 20 s");
                throw;
            }
            var map = raw as IDictionary<string, object?>;
            Assert.NotNull(map);
            return map;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The engine-level harness: the crew is built by evaluating <paramref name="js"/> (its
    /// last expression) and run from the calling thread, which is then the only drainer — the
    /// shape of the <c>globalThis.crew</c> handoff. Used when a scenario needs a CLR helper
    /// planted on the engine before the script runs.
    /// </summary>
    private static JsCrew BuildCrew(Engine engine, string js) => (JsCrew)engine.Evaluate(js).ToObject()!;

    private static Task Contend(int n, Func<Task> one)
        => Task.WhenAll(Enumerable.Range(0, n).Select(_ => Task.Run(one)));

    /// <summary>
    /// <c>crew.run()</c> reached after a top-level await runs from inside an event-loop job;
    /// its synchronous body drain cannot pump there and the script hangs (30 min timeout).
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task Crew_run_after_a_top_level_await_settles()
    {
        var r = await RunScriptAsync("""
            const warm = await Promise.resolve("warm-up");
            let stage = "never-started";
            const worker = agentBuilder().name("w").role("W").goal("g")
                .body(async (input, ctx) => { stage = "started"; const x = await ctx.llm.complete("ping"); stage = "finished:" + x; return "ok"; })
                .build();
            const crew = crewBuilder().name("c").withAgent(worker).build();
            const res = await crew.run();
            result = { stage, output: res.output, warm };
            """);
        Assert.Equal("finished:R:ping", r["stage"]);
    }

    /// <summary>The second of two sequential crew runs is the same call from inside a job.</summary>
    [Fact(Skip = Scr25)]
    public async Task Two_sequential_crew_runs_both_settle()
    {
        var r = await RunScriptAsync("""
            const mk = (n) => crewBuilder().name(n).withAgent(
                agentBuilder().name(n + "-w").role("W").goal("g")
                    .body(async (input, ctx) => await ctx.llm.complete(n)).build()).build();
            const a = await mk("one").run();
            const b = await mk("two").run();
            result = { a: a.output, b: b.output };
            """);
        Assert.Equal("R:one", r["a"]);
        Assert.Equal("R:two", r["b"]);
    }

    /// <summary>
    /// A graph whose nodes really suspend, run from a body. With a CLR traversal loop the body
    /// was drained synchronously while the graph waited asynchronously, and the async side's
    /// wake-up was lost (10 s timeout, deterministic). The loop now lives in JS
    /// (<c>JsStateGraph</c>, SCR-25 T1): every node result is a promise reaction on the thread
    /// draining the body.
    /// </summary>
    [Fact]
    public async Task State_graph_with_suspending_nodes_runs_from_a_body()
    {
        await Contend(24, async () =>
        {
            var r = await RunScriptAsync("""
                const worker = agentBuilder().name("w").role("W").goal("g")
                    .body(async (input, ctx) => {
                        const g = stateGraph({
                            name: "g",
                            nodes: {
                                a: async (s) => ({ ...s, log: s.log + (await ctx.llm.complete("a")) + "|" }),
                                b: async (s) => ({ ...s, log: s.log + (await ctx.llm.complete("b")) + "|" }),
                                c: async (s) => ({ ...s, log: s.log + (await ctx.llm.complete("c")) + "|" }),
                            },
                            edges: { [START]: "a", a: "b", b: "c", c: END },
                        });
                        const out = await g.run({ log: "" });
                        return out.log;
                    })
                    .build();
                const res = await crewBuilder().name("c").withAgent(worker).build().run();
                result = { log: res.output };
                """);
            Assert.Equal("R:a|R:b|R:c|", r["log"]);
        });
    }

    /// <summary>
    /// Same graph, <c>g.run()</c> reached after an await in the body — from inside a job. A
    /// synchronous graph drain (the fix that would make the previous scenario pass on its own)
    /// hangs here, which is why the fix is a JS trampoline and not a change of unwrap.
    /// </summary>
    [Fact]
    public async Task State_graph_run_after_an_await_in_the_body_settles()
    {
        await Contend(24, async () =>
        {
            var r = await RunScriptAsync("""
                const worker = agentBuilder().name("w").role("W").goal("g")
                    .body(async (input, ctx) => {
                        const first = await ctx.llm.complete("first");
                        const g = stateGraph({
                            name: "g",
                            nodes: { a: async (s) => ({ ...s, log: s.log + (await ctx.llm.complete("a")) + "|" }) },
                            edges: { [START]: "a", a: END },
                        });
                        const out = await g.run({ log: first + "|" });
                        return out.log;
                    })
                    .build();
                const res = await crewBuilder().name("c").withAgent(worker).build().run();
                result = { log: res.output };
                """);
            Assert.Equal("R:first|R:a|", r["log"]);
        });
    }

    /// <summary>
    /// The concurrency the DSL spec promises for <c>ctx.stateWith</c> (chapter 05): five
    /// transforms under one <c>Promise.all</c>, each suspending. The CLR closure this replaced
    /// resumed the contended callers on pool threads and invoked their transform there —
    /// NullReferenceException inside the engine, or a 10 s timeout, depending on the
    /// interleaving. The trampoline (SCR-25 T3) runs every transform as a promise reaction on
    /// the draining thread; 24× under contention is the acceptance criterion.
    /// </summary>
    [Fact]
    public async Task Concurrent_stateWith_with_suspending_transforms_serializes()
    {
        await Contend(24, async () =>
        {
            var r = await RunScriptAsync("""
                const worker = agentBuilder().name("w").role("W").goal("g")
                    .withState(() => ({ n: 0 }))
                    .body(async (input, ctx) => {
                        await Promise.all([1,2,3,4,5].map(i => ctx.stateWith(async (s) => {
                            const x = await ctx.llm.complete("s" + i);
                            return { n: s.n + 1, last: x };
                        })));
                        return String(ctx.state.n);
                    })
                    .build();
                const crew = crewBuilder().name("c").withAgent(worker).build();
                const res = await crew.run();
                result = { n: res.output };
                """);
            Assert.Equal("5", r["n"]);
        });
    }

    /// <summary>
    /// An <c>onError</c> retry with a delay resumes the second attempt on a pool thread and
    /// invokes the body there while the script thread polls the loop. The window is narrow —
    /// this passes today — so it runs live, under contention, as the detector for it.
    /// </summary>
    [Fact]
    public async Task Retry_with_delay_then_suspending_body_survives_contention()
    {
        await Contend(24, async () =>
        {
            var r = await RunScriptAsync("""
                let attempts = 0;
                const worker = agentBuilder().name("w").role("W").goal("g")
                    .body(async (input, ctx) => {
                        attempts++;
                        if (attempts === 1) throw new Error("first attempt fails");
                        const x = await ctx.llm.complete("second");
                        return x;
                    })
                    .onError((err) => ErrorAction.retry({ delay: 5, max: 3 }))
                    .build();
                const crew = crewBuilder().name("c").withAgent(worker).build();
                const res = await crew.run();
                result = { attempts, output: res.output };
                """);
            Assert.Equal("R:second", r["output"]);
        });
    }

    /// <summary>
    /// <c>topic.publish</c> reached after an await in the body runs from inside a job, and
    /// <c>JsEventTopic.DispatchSequential</c> drains the handler's promise synchronously
    /// there: a handler that awaits anything — here a bare microtask, no provider involved —
    /// never resumes (10 s timeout). The events tests pass only because their handlers
    /// contain no await at all.
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task Topic_publish_after_an_await_delivers_to_a_handler_that_suspends()
    {
        var r = await RunScriptAsync("""
            const seen = [];
            const worker = agentBuilder().name("w").role("W").goal("g")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("x");
                    t.subscribe(async (ev) => { await Promise.resolve(); seen.push(ev.value); });
                    await Promise.resolve();
                    await t.publish("v");
                    return String(seen.length);
                }).build();
            const crew = crewBuilder().name("c").withAgent(worker).build();
            const res = await crew.run();
            result = { n: res.output };
            """);
        Assert.Equal("1", r["n"]);
    }

    /// <summary>
    /// The control for the previous scenario: the same handler, suspending on the provider,
    /// delivered by a <c>publish</c> that precedes any await in the body — the drain is not
    /// nested in a job there, so it pumps. Position-dependent behaviour is the defect.
    /// </summary>
    [Fact]
    public async Task Topic_publish_before_any_await_delivers_to_a_handler_that_suspends()
    {
        var r = await RunScriptAsync("""
            const seen = [];
            const worker = agentBuilder().name("w").role("W").goal("g")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("x");
                    t.subscribe(async (ev) => { const x = await ctx.llm.complete(ev.value); seen.push(x); });
                    await t.publish("v");
                    return seen.join(",");
                }).build();
            const crew = crewBuilder().name("c").withAgent(worker).build();
            const res = await crew.run();
            result = { out: res.output };
            """);
        Assert.Equal("R:v", r["out"]);
    }

    /// <summary>
    /// FSM hooks that suspend (<c>fsm.d.ts</c> allows <c>Promise</c> from guards and hooks),
    /// sent from a body. <c>JsStateMachine.send</c> used to await them from CLR while the body
    /// was drained synchronously — the wake-up was lost (10 s timeout in
    /// <c>AwaitPromiseSettlementAsync</c>), and the <c>onEntry</c> hook plus its context object
    /// were built on a pool thread once <c>onExit</c> resumed there. <c>send</c> is a JS
    /// trampoline now (SCR-25 T2): every hook is a promise reaction on the body's drain.
    /// </summary>
    [Fact]
    public async Task Fsm_hooks_that_suspend_settle_from_a_body()
    {
        await Contend(24, async () =>
        {
            var r = await RunScriptAsync("""
                const log = [];
                const worker = agentBuilder().name("w").role("W").goal("g")
                    .body(async (input, ctx) => {
                        const fsm = stateMachine({
                            name: "m", initial: "a",
                            states: {
                                a: { onExit: async (c) => { log.push(await ctx.llm.complete("exit")); }, transitions: { go: { target: "b" } } },
                                b: { onEntry: async (c) => { log.push(await ctx.llm.complete("entry")); } },
                            },
                        });
                        const cur = await fsm.send("go");
                        return cur + "|" + log.join(",");
                    }).build();
                const crew = crewBuilder().name("c").withAgent(worker).build();
                const res = await crew.run();
                result = { out: res.output };
                """);
            Assert.Equal("b|R:exit,R:entry", r["out"]);
        });
    }

    /// <summary>
    /// An FSM guard that suspends on the provider (<c>fsm.d.ts</c>: <c>Promise&lt;boolean&gt;</c>),
    /// sent after an await in the body — from inside a job. The trampoline awaits the guard in
    /// JS: one resolving true lets the transition through, one resolving false vetoes it, and
    /// on a veto neither hook fires and <c>send</c> resolves to the unchanged state.
    /// </summary>
    [Fact]
    public async Task Fsm_guard_that_suspends_is_awaited()
    {
        await Contend(24, async () =>
        {
            var r = await RunScriptAsync("""
                const log = [];
                const worker = agentBuilder().name("w").role("W").goal("g")
                    .body(async (input, ctx) => {
                        const first = await ctx.llm.complete("first");
                        const machine = (expected) => stateMachine({
                            name: "m", initial: "a",
                            states: {
                                a: {
                                    onExit: (c) => { log.push("exit:" + expected); },
                                    transitions: { go: { target: "b", guard: async (c) => (await ctx.llm.complete("g")) === expected } },
                                },
                                b: { onEntry: (c) => { log.push("entry:" + expected); } },
                            },
                        });
                        const allowed = await machine("R:g").send("go");
                        const vetoed = await machine("R:other").send("go");
                        return first + "|" + allowed + "|" + vetoed + "|" + log.join(",");
                    }).build();
                const crew = crewBuilder().name("c").withAgent(worker).build();
                const res = await crew.run();
                result = { out: res.output };
                """);
            Assert.Equal("R:first|b|a|exit:R:g,entry:R:g", r["out"]);
        });
    }

    /// <summary>
    /// <c>ctx.spawn</c> after an await in the body attaches the agent from inside a job, and
    /// <c>JsCrew.InvokeAgentLifecycleHook</c> drains an async <c>onAgentStart</c> synchronously
    /// there (10 s timeout). <c>agent.d.ts</c> declares the hook as <c>Promise&lt;void&gt; | void</c>.
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task Spawn_after_an_await_runs_a_suspending_onAgentStart()
    {
        var r = await RunScriptAsync("""
            const log = [];
            const worker = agentBuilder().name("w").role("W").goal("g")
                .body(async (input, ctx) => {
                    await Promise.resolve();
                    ctx.spawn(agentBuilder().name("child").role("C").goal("g")
                        .onAgentStart(async (c) => { await Promise.resolve(); log.push("started"); }));
                    return log.join(",");
                }).build();
            const crew = crewBuilder().name("c").withAgent(worker).build();
            const res = await crew.run();
            result = { out: res.output };
            """);
        Assert.Equal("started", r["out"]);
    }

    /// <summary>
    /// An async <c>onCrewStart</c> hook with <c>crew.run()</c> after a top-level await:
    /// <c>JsCrew.InvokeCrewHook</c> drains it with <c>UnwrapIfPromise(ct)</c> — no timeout at
    /// all, only the run's own cancellation. The 1.5 s run timeout bounds the scenario; without
    /// it the script never returns.
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task Crew_start_hook_that_suspends_settles_when_run_follows_a_top_level_await()
    {
        var r = await RunScriptAsync("""
            const warm = await Promise.resolve("warm");
            const worker = agentBuilder().name("w").role("W").goal("g").body((input, ctx) => "ok").build();
            const crew = crewBuilder().name("c").withAgent(worker)
                .onCrewStart(async (c) => { await Promise.resolve(); })
                .build();
            const res = await crew.run({ timeout: 1500 });
            result = { out: res.output };
            """);
        Assert.Equal("ok", r["out"]);
    }

    /// <summary>
    /// <c>ctx.llm.act</c> with <c>onDelta</c> against a provider that streams: each delta is a
    /// thread-pool continuation, and <c>JsLlmFacade.ChatViaStreamAsync</c> invokes the JS
    /// callback right there — on a thread that is not the one draining the engine. Measured by
    /// thread id, so it fails deterministically instead of racing.
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task Act_onDelta_runs_on_the_thread_that_drains_the_engine()
    {
        var engine = new JsEngineFactory(llmProvider: new SlowStreamingProvider()).Create();
        engine.SetValue("__tid", new Func<int>(() => Environment.CurrentManagedThreadId));
        var crew = BuildCrew(engine, """
            const worker = agentBuilder().name("w").role("W").goal("g")
                .body(async (input, ctx) => {
                    const t0 = __tid();
                    const tids = [];
                    const r = await ctx.llm.act("p", { onDelta: (d) => { tids.push(__tid()); } });
                    return t0 + "|" + tids.join(",") + "|" + r.output;
                }).build();
            crewBuilder().name("c").withAgent(worker).build();
            """);

        var res = await crew.RunAsync(null, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        var parts = res.output!.Split('|');
        Assert.Equal("d0d1d2", parts[2]);
        Assert.All(parts[1].Split(','), tid => Assert.Equal(parts[0], tid));
    }

    /// <summary>
    /// <c>graph.runStream</c> is declared <c>AsyncIterable</c> in <c>graph.d.ts</c>. The runtime
    /// used to hand JS an <c>IAsyncEnumerable</c>, which Jint does not expose as an async
    /// iterable (<c>for await</c> failed with "The value is not iterable"); it is a JS async
    /// generator since SCR-25 T1.
    /// </summary>
    [Fact]
    public async Task Graph_runStream_is_consumable_with_for_await()
    {
        await Contend(24, async () =>
        {
            var r = await RunScriptAsync("""
                const worker = agentBuilder().name("w").role("W").goal("g")
                    .body(async (input, ctx) => {
                        const g = stateGraph({ name: "g", nodes: { a: (s) => s, b: (s) => s }, edges: { [START]: "a", a: "b", b: END } });
                        let n = 0;
                        for await (const s of g.runStream({})) n++;
                        return String(n);
                    }).build();
                const crew = crewBuilder().name("c").withAgent(worker).build();
                const res = await crew.run();
                result = { n: res.output };
                """);
            Assert.Equal("2", r["n"]);
        });
    }

    /// <summary>Same as the graph: <c>crew.runStream</c> is not iterable from JS (SCR-25 T4).</summary>
    [Fact(Skip = Scr25)]
    public async Task Crew_runStream_is_consumable_with_for_await()
    {
        var r = await RunScriptAsync("""
            const inner = crewBuilder().name("inner").withAgent(
                agentBuilder().name("i").role("W").goal("g").body((input, ctx) => "ok").build()).build();
            const worker = agentBuilder().name("w").role("W").goal("g")
                .body(async (input, ctx) => { let n = 0; for await (const e of inner.runStream()) n++; return String(n); }).build();
            const crew = crewBuilder().name("c").withAgent(worker).build();
            const res = await crew.run();
            result = { n: res.output };
            """);
        Assert.Equal("2", r["n"]);
    }
}
#pragma warning restore xUnit1004
