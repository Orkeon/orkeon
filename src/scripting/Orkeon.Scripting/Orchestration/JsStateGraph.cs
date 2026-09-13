using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Orchestration;

/// <summary>
/// Sentinels exposed to JS as <c>START</c> and <c>END</c>.
/// </summary>
public static class GraphSentinels
{
    /// <summary>String marker recognised by the runtime as the entry pseudo-node.</summary>
    public const string Start = "__START__";
    /// <summary>String marker recognised by the runtime as the exit pseudo-node.</summary>
    public const string End = "__END__";
}

/// <summary>
/// Bag of guard rails consumed by <see cref="JsStateGraph"/>. Built from the
/// <c>graphConfig</c> literal by <c>StateGraphBinding</c>.
/// </summary>
public sealed record JsGraphConfig(
    int MaxTransitions,
    int MaxStateVisits,
    int MaxRetryCycles,
    TimeSpan? MaxTotalDuration);

/// <summary>
/// Script-scoped LangGraph-style state graph produced by the <c>stateGraph(literal)</c>
/// global. Uses string node ids; <c>START</c> and <c>END</c> are reserved sentinels.
/// </summary>
/// <remarks>
/// <para>The traversal loop lives in JavaScript (SCR-25 T1). Node functions and conditional
/// edges are script code, and <c>graph.d.ts</c> lets both return a promise; a CLR loop that
/// awaited them had to call back into the engine from whatever thread its await resumed on and
/// drain their promises from inside the job that called <c>run()</c> — which Jint's
/// single-drainer event loop does not allow (a 10 s timeout from a body, a hang after the body's
/// first await). So <see cref="run"/> and <see cref="runStream"/> are JS functions built once per
/// graph from <see cref="TrampolineSource"/>: they await node and edge results as ordinary promise
/// reactions on the thread draining the loop, whichever it is, and the CLR only supplies
/// synchronous helpers — the budget, the edge table, the node table — and one awaited
/// <see cref="Task"/>, the cancellation.</para>
/// <para>Every synchronous helper throws through <see cref="JsHostError"/>: a raw CLR exception
/// from a delegate would skip the trampoline's <c>finally</c>, leave its promise pending and
/// erupt out of the drainer. The bridged Error carries the typed exception on <c>clr</c>, with
/// the same messages as before — <see cref="InvalidOperationException"/> for a budget overrun,
/// <see cref="InvalidScriptException"/> for an undeclared node or edge,
/// <see cref="OperationCanceledException"/> for a cancelled run — and
/// <see cref="JsHostError.Unwrap"/> recovers it from the rejected value. The crew boundary
/// (<c>JsCrew.RunAsync</c>) maps a cancellation of the crew's own token to a bare
/// <see cref="OperationCanceledException"/>; every other rejection, a <c>maxTotalDuration</c>
/// deadline included, reaches a C# caller as Jint's <c>PromiseRejectedException</c> carrying
/// that Error.</para>
/// </remarks>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of StateGraph in Typings/graph.d.ts; that declaration is the contract scripts read.
public sealed class JsStateGraph
{
    /// <summary>
    /// The factory the trampolines come from. Called once per graph with the CLR helpers; the
    /// object it returns holds the two script-facing functions. <c>runStream</c> owns the
    /// traversal: one hop per iteration, the handle from <c>begin()</c> released in its
    /// <c>finally</c> — reached on completion, on a throw, and when the consumer breaks out.
    /// <c>run</c> is that stream consumed to its last state. <c>stop</c> is the promise of one
    /// run's cancellation (ambient token or <c>maxTotalDuration</c>), and every await on script
    /// code races it, so a slow node or edge is abandoned when the run is cancelled and not at
    /// the next hop — what the CLR loop's token-observing await used to do. The task behind it
    /// faults on a pool thread, where nothing can build a bridged Error, so its rejection is
    /// re-raised through <c>throwIfCancelled</c> on the engine thread and the run rejects with
    /// the same typed <see cref="OperationCanceledException"/> as a hop check. The check at the
    /// top of each hop covers the synchronous case, where the race would never see the
    /// rejection first.
    /// </summary>
    internal const string TrampolineSource = """
        (begin, end, cancellation, edge, resolveTarget, node, enforceBudget, throwIfCancelled, START, END) => {
            async function* runStream(initial) {
                const handle = begin();
                try {
                    const stop = cancellation(handle).catch(() => { throwIfCancelled(handle); });
                    const wait = (value) => Promise.race([value, stop]);
                    const next = async (from, state) => {
                        const e = edge(from);
                        return resolveTarget(from, typeof e === "function" ? await wait(e(state)) : e);
                    };
                    let state = initial;
                    let current = await next(START, state);
                    while (current !== END) {
                        throwIfCancelled(handle);
                        enforceBudget(handle, current);
                        const from = current;
                        state = await wait(node(current)(state));
                        current = await next(current, state);
                        yield { fromNode: from, toNode: current === END ? "END" : current, state };
                    }
                } finally { end(handle); }
            }
            const run = async (initial) => {
                let state = initial;
                for await (const hop of runStream(initial)) state = hop.state;
                return state;
            };
            return { run, runStream };
        }
        """;

    private readonly Engine _engine;
    private readonly Dictionary<string, JsValue> _nodes;
    private readonly Dictionary<string, JsValue> _edges;
    private readonly JsGraphConfig _config;
    private readonly Func<CancellationToken>? _ambientCt;
    private JsValue? _trampolines;

    public string name { get; }

    internal JsStateGraph(
        Engine engine,
        string name,
        Dictionary<string, JsValue> nodes,
        Dictionary<string, JsValue> edges,
        JsGraphConfig config,
        Func<CancellationToken>? ambientCt = null)
    {
        _engine = engine;
        this.name = name;
        _nodes = nodes;
        _edges = edges;
        _config = config;
        _ambientCt = ambientCt;
    }

    /// <summary>JS <c>async (initial) =&gt; finalState</c>; see the class remarks.</summary>
    public JsValue run => Trampolines.Get("run");

    /// <summary>JS async generator yielding <c>{ fromNode, toNode, state }</c> per hop; see the class remarks.</summary>
    public JsValue runStream => Trampolines.Get("runStream");

    private JsValue Trampolines => _trampolines ??= BuildTrampolines();

    private JsValue BuildTrampolines()
    {
        Func<GraphRun> begin = () => JsHostError.Guard(_engine,
            () => new GraphRun(_config.MaxTotalDuration, _ambientCt?.Invoke() ?? CancellationToken.None));
        Action<GraphRun> end = run => JsHostError.Guard(_engine, run.Dispose);
        Func<GraphRun, Task<JsValue>> cancellation = run => run.Cancellation;
        Func<string, JsValue> edge = from => JsHostError.Guard(_engine, () => ResolveEdge(from));
        Func<string, JsValue, string> resolveTarget = (from, target) => JsHostError.Guard(_engine, () => ResolveTarget(from, target));
        Func<string, JsValue> node = current => JsHostError.Guard(_engine, () => ResolveNode(current));
        Action<GraphRun, string> enforceBudget = (run, current) => JsHostError.Guard(_engine, () => EnforceBudget(run, current));
        Action<GraphRun> throwIfCancelled = run => JsHostError.Guard(_engine, () => run.Token.ThrowIfCancellationRequested());

        var factory = JsTrampolineFactories.Graph.For(_engine);
        return _engine.Invoke(factory,
            [begin, end, cancellation, edge, resolveTarget, node, enforceBudget, throwIfCancelled, GraphSentinels.Start, GraphSentinels.End]);
    }

    private JsValue ResolveNode(string current)
    {
        if (!_nodes.TryGetValue(current, out var nodeFn))
            throw new InvalidScriptException($"stateGraph node '{current}' not declared.");
        return nodeFn;
    }

    /// <summary>The raw edge out of <paramref name="from"/>: a node id, or the function the trampoline calls (and awaits).</summary>
    private JsValue ResolveEdge(string from)
    {
        if (!_edges.TryGetValue(from, out var edge))
            throw new InvalidScriptException($"stateGraph has no edge from '{from}'.");
        if (!edge.IsString() && edge is not Jint.Native.Function.Function)
            throw new InvalidScriptException($"stateGraph edge from '{from}' has unsupported type.");
        return edge;
    }

    /// <summary>
    /// Validates what the edge out of <paramref name="from"/> produced. A string edge is its own
    /// target, so a non-string here can only come from a conditional edge.
    /// </summary>
    private string ResolveTarget(string from, JsValue target)
    {
        if (!target.IsString())
            throw new InvalidScriptException(
                $"stateGraph conditional edge from '{from}' must return a string node id.");

        var id = target.AsString();
        if (id == GraphSentinels.End) return GraphSentinels.End;
        if (!_nodes.ContainsKey(id))
            throw new InvalidScriptException($"stateGraph edge from '{from}' targets undeclared node '{id}'.");
        return id;
    }

    private void EnforceBudget(GraphRun run, string current)
    {
        run.Transitions++;
        if (run.Transitions > _config.MaxTransitions)
            throw new InvalidOperationException(
                $"stateGraph '{name}' exceeded maxTransitions={_config.MaxTransitions}.");

        if (!run.SeenOnce.Add(current)) run.RetryCycles++;
        if (run.RetryCycles > _config.MaxRetryCycles)
            throw new InvalidOperationException(
                $"stateGraph '{name}' exceeded maxRetryCycles={_config.MaxRetryCycles}.");

        var visitCount = run.Visits.TryGetValue(current, out var v) ? v + 1 : 1;
        run.Visits[current] = visitCount;
        if (visitCount > _config.MaxStateVisits)
            throw new InvalidOperationException(
                $"stateGraph '{name}' node '{current}' exceeded maxStateVisits={_config.MaxStateVisits}.");
    }

    /// <summary>
    /// The CLR side of one traversal: the budget counters, the token the hops check, and the
    /// task that faults when that token fires (the <c>stop</c> promise of the trampoline).
    /// Created by <c>begin()</c>, handed back opaque to the per-run helpers, disposed by
    /// <c>end()</c> in the trampoline's <c>finally</c>.
    /// </summary>
    private sealed class GraphRun : IDisposable
    {
        private readonly CancellationTokenSource? _duration;
        private readonly CancellationTokenSource? _linked;
        private readonly TaskCompletionSource<JsValue> _cancelled = new();
        private readonly CancellationTokenRegistration _registration;

        public GraphRun(TimeSpan? maxTotalDuration, CancellationToken ambient)
        {
            if (maxTotalDuration is { } limit)
            {
                _duration = new CancellationTokenSource(limit);
                _linked = CancellationTokenSource.CreateLinkedTokenSource(ambient, _duration.Token);
                Token = _linked.Token;
            }
            else
            {
                Token = ambient;
            }

            // Faulted rather than cancelled so that Jint rejects the promise (a cancelled Task is
            // reported as its own ExecutionCanceledException); the trampoline re-raises the
            // rejection as the typed OperationCanceledException from the engine thread. Jint's
            // bridge enqueues that rejection inline, on the cancelling thread — it only enqueues
            // and signals there — so the loop is told the instant the token fires, with no pool
            // thread in between (CrewRunScope explains the starvation this avoids).
            _registration = Token.Register(
                static (state, token) => ((GraphRun)state!)._cancelled.TrySetException(new OperationCanceledException(token)),
                this);
        }

        public CancellationToken Token { get; }
        public Task<JsValue> Cancellation => _cancelled.Task;
        public Dictionary<string, int> Visits { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SeenOnce { get; } = new(StringComparer.Ordinal);
        public int RetryCycles { get; set; }
        public int Transitions { get; set; }

        public void Dispose()
        {
            _registration.Dispose();
            _linked?.Dispose();
            _duration?.Dispose();
        }
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
