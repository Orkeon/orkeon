using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Orchestration;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Orchestration;

public sealed class StateGraphTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();
    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

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
        var result = await graph.run(input);

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
        var bigResult = await graph.run(bigInput);
        Assert.Equal("BIG", bigResult.Get("label").AsString());

        var smallInput = await engine.EvaluateAsync("({ value: 5 })", cancellationToken: TestContext.Current.CancellationToken);
        var smallResult = await graph.run(smallInput);
        Assert.Equal("SMALL", smallResult.Get("label").AsString());
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

        var chunks = new List<object>();
        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        await foreach (var c in graph.runStream(input, CancellationToken.None))
            chunks.Add(c);

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
            () => graph.run(input));
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

        var chunks = new List<object>();
        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);
        await foreach (var c in graph.runStream(input, CancellationToken.None))
            chunks.Add(c);

        Assert.Equal(2, chunks.Count);
        dynamic first = chunks[0];
        dynamic second = chunks[1];
        Assert.Equal("a", (string)first.fromNode);
        Assert.Equal("b", (string)first.toNode);
        Assert.NotNull(first.state);
        Assert.Equal("b", (string)second.fromNode);
        Assert.Equal("END", (string)second.toNode);
        Assert.NotNull(second.state);
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
            () => graph.run(input));
        // The Strict preset trips its first breaker (maxRetryCycles=1) well before
        // maxTransitions=50 would. Either retry-cycle or state-visit triggering proves
        // the preset was actually applied — they are the differentiating fields vs the
        // default preset.
        Assert.Matches("maxRetryCycles|maxStateVisits", ex.Message);
    }
}
