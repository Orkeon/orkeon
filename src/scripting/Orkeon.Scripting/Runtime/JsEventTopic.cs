using System.Collections.Concurrent;
using Jint;
using Jint.Native;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Script-scoped pub/sub topic exposed to JS as <c>ctx.events.topic(name, opts)</c>.
/// Subscribers are invoked sequentially or in parallel depending on the mode.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsEventTopic
{
    private readonly Engine _engine;
    private readonly string _name;
    private readonly bool _parallel;
    private readonly List<(JsValue Handler, string? AgentId)> _handlers = new();
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventLocks = new(StringComparer.Ordinal);

    /// <summary>Optional back-reference set by the broker so subscribe can attribute the agent.</summary>
    internal JsEventBroker? Broker { get; set; }

    public string name => _name;
    public string mode => _parallel ? "parallel" : "sequential";

    internal JsEventTopic(Engine engine, string name, bool parallel)
    {
        _engine = engine;
        _name = name;
        _parallel = parallel;
    }

    public JsSubscription subscribe(JsValue handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var agentId = Broker?.CurrentAgentId;
        lock (_lock) _handlers.Add((handler, agentId));
        return new JsSubscription(() =>
        {
            lock (_lock) _handlers.RemoveAll(h => ReferenceEquals(h.Handler, handler));
        });
    }

    /// <summary>Returns the current handler count (used by tests / DetachAgent assertions).</summary>
    internal int HandlerCount { get { lock (_lock) return _handlers.Count; } }

    /// <summary>Removes every handler attributed to <paramref name="agentId"/>.</summary>
    internal void DetachAgent(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;
        lock (_lock)
            _handlers.RemoveAll(h => string.Equals(h.AgentId, agentId, StringComparison.Ordinal));
    }

    public Func<JsValue, Task> publish => async value =>
    {
        JsValue[] snapshot;
        lock (_lock) snapshot = _handlers.Select(h => h.Handler).ToArray();
        if (snapshot.Length == 0) return;

        var ev = new JsPublishedEvent(_engine, value, snapshot.Length, _eventLocks);

        if (_parallel)
        {
            DispatchParallel(snapshot, ev);
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        DispatchSequential(snapshot, ev);
    };

    private void DispatchParallel(JsValue[] snapshot, JsPublishedEvent ev)
    {
        // Dispatch every handler synchronously on the engine thread (Jint is
        // single-threaded so concurrent _engine.Invoke would race). Collect any
        // returned promises and let them resolve together via the JS event loop —
        // that is where actual parallelism shows up when handlers await I/O.
        var pending = new List<JsValue>();
        for (var i = 0; i < snapshot.Length; i++)
        {
            ev.handlerCount = i + 1;
            var raw = _engine.Invoke(snapshot[i], [ev]);
            if (raw.IsPromise()) pending.Add(raw);
        }
        foreach (var p in pending) p.UnwrapIfPromise();
    }

    private void DispatchSequential(JsValue[] snapshot, JsPublishedEvent ev)
    {
        for (var i = 0; i < snapshot.Length; i++)
        {
            ev.handlerCount = i + 1;
            var result = _engine.Invoke(snapshot[i], [ev]);
            if (result.IsPromise()) result.UnwrapIfPromise();
            if (ev.ShouldStop) break;
        }
    }
}

public sealed class JsSubscription
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    internal JsSubscription(Action unsubscribe) { _unsubscribe = unsubscribe; }

    public void unsubscribe()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
