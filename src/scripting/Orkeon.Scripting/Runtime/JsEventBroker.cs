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
#pragma warning disable CS1591 // JS-interop mirror of EventBroker in Typings/events.d.ts; that declaration is the contract scripts read.
public sealed class JsEventBroker
{
    private readonly Engine _engine;
    private readonly ConcurrentDictionary<string, JsEventQueue> _queues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, JsEventTopic> _topics = new(StringComparer.Ordinal);

    internal JsEventBroker(Engine engine) { _engine = engine; }

    /// <summary>
    /// The agent whose body attempt opened most recently and is still open, set by
    /// <see cref="JsCrew.BeginBrokerScope"/> and recomputed when an attempt closes, so that
    /// <see cref="JsEventTopic.subscribe"/> can attribute a new subscription to the agent that called
    /// it and a nested <c>runAgent</c> hands attribution back to the body that called it; null on an
    /// idle crew, whatever order the attempts closed in. One crew-wide value: two runs of one crew
    /// interleaved on one event loop (<c>Promise.all([crew.run(), crew.run()])</c>) share it, so the
    /// attribution — and the <c>runAgent</c> re-entrance guard built on it — is best-effort under such
    /// interleaving.
    /// </summary>
    internal string? CurrentAgentId { get; set; }

    /// <summary>
    /// The token of the run whose attempt <see cref="CurrentAgentId"/> names, none on an idle crew.
    /// Surfaces to <c>stateGraph.run</c> and to a topic handler's <c>ev.lock</c> so a script-level
    /// cancellation aborts them without an explicit signal argument, and to a run opened from a body
    /// (<see cref="JsCrew.AmbientToken"/>). Same crew-wide, best-effort caveat as the agent id.
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
