using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
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
/// into the engine — a node function, an agent body, a state transform — from a thread
/// that is not the one draining the event loop, or it drains synchronously from inside a
/// job. Jint's loop is exclusive per drain but has no guard on <c>Engine.Invoke</c>, and a
/// drain nested inside a job cannot pump ("Nested inside a job it cannot pump", Jint
/// <c>Engine.DrainEventLoopUntil</c>). Mixing a synchronous drain (<c>JsCrew.UnwrapPromise</c>)
/// with an asynchronous one (<c>JsStateGraph.run</c>) loses the async side's wake-up, and
/// a synchronous drain reached from inside a job never settles.</para>
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

    private static async Task<IDictionary<string, object?>> RunScriptAsync(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "orkeon-threading-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "probe.ork.ts"), source);
            var fs = new DiskBackedFileSystemService(dir, "/scripts");
            var host = new ScriptHost(fs, new JsEngineFactory(llmProvider: new SlowProvider()));

            var run = host.RunAsync("/scripts/probe.ork.ts", CancellationToken.None);
            var winner = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(ReferenceEquals(winner, run), "HANG: the script did not settle within 20 s");
            var map = (await run) as IDictionary<string, object?>;
            Assert.NotNull(map);
            return map;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

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
    /// A graph whose nodes really suspend, run from a body: the body is drained synchronously,
    /// the graph waits asynchronously, and the async side's wake-up is lost (10 s timeout).
    /// Deterministic, no contention needed.
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task State_graph_with_suspending_nodes_runs_from_a_body()
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
    }

    /// <summary>
    /// Same graph, <c>g.run()</c> reached after an await in the body — from inside a job. A
    /// synchronous graph drain (the fix that makes the previous scenario pass) hangs here,
    /// which is why the fix has to be a JS trampoline and not a change of unwrap.
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task State_graph_run_after_an_await_in_the_body_settles()
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
    }

    /// <summary>
    /// The concurrency the DSL spec promises for <c>ctx.stateWith</c> (chapter 05): five
    /// transforms under one <c>Promise.all</c>, each suspending. The contended callers resume
    /// on pool threads and invoke their transform there — NullReferenceException inside the
    /// engine, or a 10 s timeout, depending on the interleaving.
    /// </summary>
    [Fact(Skip = Scr25)]
    public async Task Concurrent_stateWith_with_suspending_transforms_serializes()
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
}
#pragma warning restore xUnit1004
