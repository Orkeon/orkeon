using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;

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
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of StateGraph in Typings/graph.d.ts; that declaration is the contract scripts read.
public sealed class JsStateGraph
{
    private readonly Engine _engine;
    private readonly Dictionary<string, JsValue> _nodes;
    private readonly Dictionary<string, JsValue> _edges;
    private readonly JsGraphConfig _config;
    private readonly Func<CancellationToken>? _ambientCt;

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

    public Func<JsValue, Task<JsValue>> run => async initial =>
    {
        var ct = _ambientCt?.Invoke() ?? CancellationToken.None;
        using var durationCts = CreateDurationCts();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct, durationCts?.Token ?? CancellationToken.None);

        var guard = new GraphGuard();
        var state = initial ?? JsValue.Undefined;
        var current = ResolveTarget(GraphSentinels.Start, state);
        while (!string.Equals(current, GraphSentinels.End, StringComparison.Ordinal))
        {
            linked.Token.ThrowIfCancellationRequested();
            EnforceBudget(guard, current);
            var nodeFn = ResolveNode(current);
            var raw = _engine.Invoke(nodeFn, [state]);
            state = raw.IsPromise()
                ? await raw.UnwrapIfPromiseAsync(linked.Token).ConfigureAwait(false)
                : raw;
            current = ResolveTarget(current, state);
        }
        return state;
    };

    public async IAsyncEnumerable<object> runStream(JsValue initial,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var durationCts = CreateDurationCts();
        var ambient = _ambientCt?.Invoke() ?? CancellationToken.None;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct, ambient, durationCts?.Token ?? CancellationToken.None);

        var guard = new GraphGuard();
        var state = initial ?? JsValue.Undefined;
        var current = ResolveTarget(GraphSentinels.Start, state);
        while (!string.Equals(current, GraphSentinels.End, StringComparison.Ordinal))
        {
            linked.Token.ThrowIfCancellationRequested();
            EnforceBudget(guard, current);
            var nodeFn = ResolveNode(current);
            var from = current;
            var raw = _engine.Invoke(nodeFn, [state]);
            state = raw.IsPromise()
                ? await raw.UnwrapIfPromiseAsync(linked.Token).ConfigureAwait(false)
                : raw;
            current = ResolveTarget(current, state);
            yield return new
            {
                fromNode = from,
                toNode = current == GraphSentinels.End ? "END" : current,
                state = state.ToObject(),
            };
        }
    }

    private CancellationTokenSource? CreateDurationCts()
        => _config.MaxTotalDuration is not null
            ? new CancellationTokenSource(_config.MaxTotalDuration.Value)
            : null;

    private JsValue ResolveNode(string current)
    {
        if (!_nodes.TryGetValue(current, out var nodeFn))
            throw new InvalidScriptException($"stateGraph node '{current}' not declared.");
        return nodeFn;
    }

    private void EnforceBudget(GraphGuard guard, string current)
    {
        guard.Transitions++;
        if (guard.Transitions > _config.MaxTransitions)
            throw new InvalidOperationException(
                $"stateGraph '{name}' exceeded maxTransitions={_config.MaxTransitions}.");

        if (!guard.SeenOnce.Add(current)) guard.RetryCycles++;
        if (guard.RetryCycles > _config.MaxRetryCycles)
            throw new InvalidOperationException(
                $"stateGraph '{name}' exceeded maxRetryCycles={_config.MaxRetryCycles}.");

        var visitCount = guard.Visits.TryGetValue(current, out var v) ? v + 1 : 1;
        guard.Visits[current] = visitCount;
        if (visitCount > _config.MaxStateVisits)
            throw new InvalidOperationException(
                $"stateGraph '{name}' node '{current}' exceeded maxStateVisits={_config.MaxStateVisits}.");
    }

    private sealed class GraphGuard
    {
        public Dictionary<string, int> Visits { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SeenOnce { get; } = new(StringComparer.Ordinal);
        public int RetryCycles { get; set; }
        public int Transitions { get; set; }
    }

    private string ResolveTarget(string from, JsValue state)
    {
        if (!_edges.TryGetValue(from, out var edge))
            throw new InvalidScriptException($"stateGraph has no edge from '{from}'.");

        string target;
        if (edge.IsString())
            target = edge.AsString();
        else if (edge is Jint.Native.Function.Function)
        {
            var raw = _engine.Invoke(edge, [state]);
            if (raw.IsPromise()) raw = raw.UnwrapIfPromise();
            if (!raw.IsString())
                throw new InvalidScriptException(
                    $"stateGraph conditional edge from '{from}' must return a string node id.");
            target = raw.AsString();
        }
        else
        {
            throw new InvalidScriptException($"stateGraph edge from '{from}' has unsupported type.");
        }

        if (target == GraphSentinels.End) return GraphSentinels.End;
        if (!_nodes.ContainsKey(target))
            throw new InvalidScriptException($"stateGraph edge from '{from}' targets undeclared node '{target}'.");
        return target;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
