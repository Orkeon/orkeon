using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Orchestration;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Orchestration;

/// <summary>
/// The graph is exercised through the surface a script sees — <c>run</c> and <c>runStream</c>
/// are JS functions (SCR-25 T1) — invoked with the engine at rest, so the test thread is the
/// only drainer. A rejection whose Error carries a CLR exception (<see cref="JsHostError"/>)
/// is rethrown typed, the way the crew boundary surfaces it to a script's caller.
/// </summary>
public sealed class StateGraphTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();
    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;
    private static JsValue Fn(Engine engine, string js) => engine.Evaluate(js);

    private static async Task<JsValue> RunAsync(Engine engine, JsStateGraph graph, JsValue input)
    {
        var promise = engine.Invoke(graph.run, input);
        try
        {
            return await promise.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);
        }
        catch (PromiseRejectedException ex) when (JsHostError.Unwrap(ex.RejectedValue) is { } clr)
        {
            throw clr;
        }
    }

    /// <summary>Consumes <c>runStream</c> the way a script does, with <c>for await</c>, and returns the hops.</summary>
    private static async Task<List<JsValue>> StreamAsync(Engine engine, JsStateGraph graph, JsValue input)
    {
        var collect = Fn(engine, """
            async (graph, input) => {
                const hops = [];
                for await (const hop of graph.runStream(input)) hops.push(hop);
                return hops;
            }
            """);
        var promise = engine.Invoke(collect, graph, input);
        var hops = await promise.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);
        return [.. hops.AsArray()];
    }

    [Fact]
    public async Task StateGraph_linear_path_runs_to_END_returning_final_state()
    {
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "linear",
                nodes: {
                    a: (s) => ({ ...s, hits: (s.hits||0) + 1 }),
                    b: (s) => ({ ...s, hits: s.hits + 10 }),
                },
                edges: {
                    [START]: "a",
                    a: "b",
                    b: END,
                }
            });
            """);

        var input = await engine.EvaluateAsync("({ hits: 0 })", cancellationToken: TestContext.Current.CancellationToken);
        var result = await RunAsync(engine, graph, input);

        Assert.Equal(11d, Convert.ToDouble(result.Get("hits").ToObject()));
    }

    [Fact]
    public async Task StateGraph_conditional_edge_branches_based_on_state()
    {
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "branch",
                nodes: {
                    classify: (s) => ({ ...s, kind: s.value > 10 ? "big" : "small" }),
                    big: (s) => ({ ...s, label: "BIG" }),
                    small: (s) => ({ ...s, label: "SMALL" }),
                },
                edges: {
                    [START]: "classify",
                    classify: (s) => s.kind,
                    big: END,
                    small: END,
                }
            });
            """);

        var bigInput = await engine.EvaluateAsync("({ value: 42 })", cancellationToken: TestContext.Current.CancellationToken);
        var bigResult = await RunAsync(engine, graph, bigInput);
        Assert.Equal("BIG", bigResult.Get("label").AsString());

        var smallInput = await engine.EvaluateAsync("({ value: 5 })", cancellationToken: TestContext.Current.CancellationToken);
        var smallResult = await RunAsync(engine, graph, smallInput);
        Assert.Equal("SMALL", smallResult.Get("label").AsString());
    }

    [Fact]
    public async Task StateGraph_async_conditional_edge_is_awaited()
    {
        // graph.d.ts lets an edge return a Promise<GraphNode>; the trampoline awaits it like a
        // node. The CLR loop it replaces drained it synchronously (a hang from inside a job).
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "async-edge",
                nodes: {
                    classify: (s) => ({ ...s, kind: s.value > 10 ? "big" : "small" }),
                    big: (s) => ({ ...s, label: "BIG" }),
                    small: (s) => ({ ...s, label: "SMALL" }),
                },
                edges: {
                    [START]: "classify",
                    classify: async (s) => { await Promise.resolve(); return s.kind; },
                    big: END,
                    small: END,
                }
            });
            """);

        var input = await engine.EvaluateAsync("({ value: 42 })", cancellationToken: TestContext.Current.CancellationToken);
        var result = await RunAsync(engine, graph, input);
        Assert.Equal("BIG", result.Get("label").AsString());
    }

    [Fact]
    public void StateGraph_missing_START_edge_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                stateGraph({
                    name: "x",
                    nodes: { a: (s) => s },
                    edges: { a: END },
                });
                """),
            "START");

        Assert.NotNull(ex);
    }

    [Fact]
    public void StateGraph_undeclared_target_throws_InvalidScriptException_at_construction()
    {
        // After SCR-17 §4, string-edge targets are validated when the graph is built,
        // not only when run() reaches them. Conditional (function) edges are still
        // validated at run-time.
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                stateGraph({
                    name: "x",
                    nodes: { a: (s) => s },
                    edges: { [START]: "a", a: "ghost" },
                });
                """),
            "ghost");

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task StateGraph_conditional_edge_to_undeclared_node_rejects_typed_at_run_time()
    {
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "x",
                nodes: { a: (s) => s },
                edges: { [START]: "a", a: (s) => "ghost" },
            });
            """);

        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidScriptException>(() => RunAsync(engine, graph, input));
        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public async Task StateGraph_runStream_yields_one_chunk_per_transition()
    {
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "stream",
                nodes: {
                    a: (s) => ({ step: 1 }),
                    b: (s) => ({ step: 2 }),
                },
                edges: { [START]: "a", a: "b", b: END },
            });
            """);

        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        var chunks = await StreamAsync(engine, graph, input);

        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task StateGraph_runaway_cycle_aborts_at_maxTransitions()
    {
        // The conditional edge on 'b' makes END statically reachable so the new
        // boot-time reachability check is satisfied; at run-time the branch always
        // sends us back to 'a' to exercise the maxTransitions guard.
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "loop",
                nodes: {
                    a: (s) => ({ ...s, n: (s.n || 0) + 1 }),
                    b: (s) => s,
                },
                edges: { [START]: "a", a: "b", b: (s) => s.n < 999 ? "a" : END },
                graphConfig: { maxTransitions: 5, maxStateVisits: 999, maxRetryCycles: 999 },
            });
            """);

        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(engine, graph, input));
        Assert.Contains("maxTransitions", ex.Message);
    }

    [Fact]
    public void StateGraph_no_path_to_END_throws_InvalidScriptException_at_construction()
    {
        // Pure cycle with no END terminator. The BFS reachability check at build time
        // must reject the graph regardless of maxTransitions.
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                stateGraph({
                    name: "deadloop",
                    nodes: { a: (s) => s },
                    edges: { [START]: "a", a: "a" },
                });
                """),
            "START to END");

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task StateGraph_runStream_chunks_have_fromNode_toNode_state_fields()
    {
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "stream-fields",
                nodes: {
                    a: (s) => ({ step: "A" }),
                    b: (s) => ({ step: "B" }),
                },
                edges: { [START]: "a", a: "b", b: END },
            });
            """);

        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        var chunks = await StreamAsync(engine, graph, input);

        Assert.Equal(2, chunks.Count);
        var first = chunks[0];
        var second = chunks[1];
        Assert.Equal("a", first.Get("fromNode").AsString());
        Assert.Equal("b", first.Get("toNode").AsString());
        Assert.Equal("A", first.Get("state").Get("step").AsString());
        Assert.Equal("b", second.Get("fromNode").AsString());
        Assert.Equal("END", second.Get("toNode").AsString());
        Assert.Equal("B", second.Get("state").Get("step").AsString());
    }

    [Fact]
    public async Task StateGraph_runStream_break_stops_the_walk_and_leaves_the_graph_reusable()
    {
        // `break` inside `for await` closes the generator: the walk must stop after the hop
        // that was consumed — no further node runs — and the graph must stay usable for a
        // full run afterwards. The handle release in the generator's `finally` is not
        // observable from here (each run gets its own); what is observed is the walk.
        var engine = NewEngine();
        var visited = new List<string>();
        engine.SetValue("__visit", new Action<string>(visited.Add));
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "early",
                nodes: {
                    a: (s) => { __visit("a"); return s; },
                    b: (s) => { __visit("b"); return s; },
                    c: (s) => { __visit("c"); return s; },
                },
                edges: { [START]: "a", a: "b", b: "c", c: END },
            });
            """);
        var breakAfterFirst = Fn(engine, """
            async (graph) => {
                let hops = 0;
                for await (const hop of graph.runStream({})) { hops++; break; }
                return hops;
            }
            """);

        var hops = await engine.Invoke(breakAfterFirst, graph).UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, (int)hops.AsNumber());
        Assert.Equal(["a"], visited);

        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        await RunAsync(engine, graph, input);
        Assert.Equal(["a", "a", "b", "c"], visited);
    }

    [Fact]
    public async Task StateGraph_run_signal_cancellation_aborts_mid_transition()
    {
        // The graph is built inside an agent body so __orkeon_broker is planted on the
        // engine and the ambient CT is the body's. We cancel the crew CT from outside
        // and expect run() to throw OperationCanceledException at the next transition.
        // 'again' has a conditional edge to satisfy the reachability check.
        var engine = NewEngine();
        engine.SetValue("__sleep", new Func<double, Task<JsValue>>(async ms =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(ms)).ConfigureAwait(false);
            return JsValue.Undefined;
        }));
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const g = stateGraph({
                        name: "cancel",
                        nodes: {
                            slow: async (s) => { await __sleep(500); return s; },
                            again: (s) => s,
                        },
                        edges: {
                            [START]: "slow",
                            slow: "again",
                            again: (s) => "slow",  // conditional → reachability check passes
                        },
                        graphConfig: { maxTransitions: 9999, maxStateVisits: 9999, maxRetryCycles: 9999 },
                    });
                    return await g.run({});
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => crew.RunAsync(null, cts.Token));
    }

    [Fact]
    public async Task StateGraph_maxTotalDuration_abandons_a_slow_node_and_rejects_typed()
    {
        // The wall-clock bound is the graph's own token, not the crew's: the run must stop waiting
        // on the node at the deadline and reject with the OperationCanceledException the deadline
        // raised, not settle on the next hop. The node sleeps 60 s — far longer than any stall a
        // saturated pool can add (a 200 ms deadline was once measured firing late enough for a 1 s
        // node to finish first under the 24-script contention harness) — so the rejection alone
        // proves the deadline ended the run; the 30 s ceiling turns a run that waited the node out
        // into a failure instead of a minute-long wait. The sleep is not cancelled: the race in the
        // trampoline abandons the node, and its Task completes on its own afterwards.
        var engine = NewEngine();
        engine.SetValue("__sleep", new Func<double, Task<JsValue>>(async ms =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(ms)).ConfigureAwait(false);
            return JsValue.Undefined;
        }));
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "deadline",
                nodes: { slow: async (s) => { await __sleep(60000); return s; } },
                edges: { [START]: "slow", slow: END },
                graphConfig: { maxTotalDurationSeconds: 0.2 },
            });
            """);

        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunAsync(engine, graph, input).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StateGraph_circuit_breaker_preset_blocks_runaway()
    {
        // 'Strict' preset has maxStateVisits=3, so the loop on 'a' should abort well
        // before maxTransitions=50 even though the graph statically targets END.
        var engine = NewEngine();
        var graph = Eval<JsStateGraph>(engine, """
            stateGraph({
                name: "preset",
                nodes: {
                    a: (s) => ({ ...s, n: (s.n||0)+1 }),
                    b: (s) => s,
                },
                edges: { [START]: "a", a: (s) => s.n < 999 ? "a" : "b", b: END },
                graphConfig: { circuitBreakerPreset: "Strict" },
            });
            """);

        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(engine, graph, input));
        // The Strict preset trips its first breaker (maxRetryCycles=1) well before
        // maxTransitions=50 would. Either retry-cycle or state-visit triggering proves
        // the preset was actually applied — they are the differentiating fields vs the
        // default preset.
        Assert.Matches("maxRetryCycles|maxStateVisits", ex.Message);
    }
}
