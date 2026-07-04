using System.Collections.Concurrent;
using Jint;
using Jint.Native;
// IsObject/IsString live in Jint root namespace's extension methods.

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Script-scoped event broker exposed as <c>ctx.events</c>. Per-crew lifetime: queues
/// and topics are created lazily by name and shared across all agents in the crew.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsEventBroker
{
    private readonly Engine _engine;
    private readonly ConcurrentDictionary<string, JsEventQueue> _queues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, JsEventTopic> _topics = new(StringComparer.Ordinal);

    internal JsEventBroker(Engine engine) { _engine = engine; }

    /// <summary>
    /// Set by <see cref="JsCrew"/> while a body is executing so that
    /// <see cref="JsEventTopic.subscribe"/> can attribute the new subscription to the
    /// agent that called it. The per-agent mutex + the Jint engine being single-threaded
    /// guarantees at most one body is active at any moment.
    /// </summary>
    internal string? CurrentAgentId { get; set; }

    /// <summary>
    /// Cancellation token bound to the active body. Surfaces to <c>stateGraph.run</c>
    /// so a script-level <c>cts.Cancel()</c> can abort an in-flight transition without
    /// requiring an explicit signal argument.
    /// </summary>
    internal CancellationToken CurrentCt { get; set; } = CancellationToken.None;

    public JsEventQueue queue(string name)
        => _queues.GetOrAdd(name, n => new JsEventQueue(n));

    public JsEventTopic topic(string name)
        => _topics.GetOrAdd(name, n => new JsEventTopic(_engine, n, parallel: false) { Broker = this });

    public JsEventTopic topic(string name, JsValue opts)
    {
        var parallel = false;
        if (opts is not null && opts.IsObject())
        {
            var mode = opts.Get("mode");
            if (mode.IsString())
                parallel = string.Equals(mode.AsString(), "parallel", StringComparison.OrdinalIgnoreCase);
        }
        return _topics.GetOrAdd(name, n => new JsEventTopic(_engine, n, parallel) { Broker = this });
    }

    /// <summary>
    /// Removes every subscription owned by <paramref name="agentId"/> across all topics.
    /// Queues are crew-scoped and are intentionally left untouched.
    /// </summary>
    internal void DetachAgent(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;
        foreach (var t in _topics.Values) t.DetachAgent(agentId);
    }

    /// <summary>Returns the topic currently registered under <paramref name="name"/>, or null.</summary>
    internal JsEventTopic? TryGetTopic(string name)
        => _topics.TryGetValue(name, out var t) ? t : null;
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
