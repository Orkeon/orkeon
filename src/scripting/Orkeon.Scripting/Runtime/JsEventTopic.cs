using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Script-scoped pub/sub topic exposed to JS as <c>ctx.events.topic(name, opts)</c>.
/// Subscribers are invoked sequentially or in parallel depending on the mode.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of EventTopic/Subscription in Typings/events.d.ts; that declaration is the contract scripts read.
public sealed class JsEventTopic
{
    private readonly Engine _engine;
    private readonly string _name;
    private readonly bool _parallel;
    private readonly List<(JsValue Handler, string? AgentId)> _handlers = new();
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventLocks = new(StringComparer.Ordinal);
    private JsValue? _publishJs;

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
        // Refused here, where the script can see which topic and which call it was, rather
        // than by the delivery loop as a bare TypeError on `handlers[i]` at publish time.
        // Bridged: a body past its first await calls this from an event-loop job, where a raw
        // CLR throw would skip the script's catch and finally (see JsHostError).
        if (handler is not Jint.Native.Function.Function)
            throw JsHostError.Wrap(_engine, new InvalidScriptException($"topic('{_name}').subscribe(handler) requires a function."));
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

    // Implemented in JS (SCR-25 T5): the delivery loop calls back into the handlers, so it
    // has to live where the handlers live. Driven from C#, publish invoked each handler and
    // drained its promise synchronously, which cannot pump when publish is reached from
    // inside an event-loop job — i.e. after any await in the calling body — and the
    // asynchronous alternative would resume on a pool thread and invoke JS there. As an
    // async JS function, every handler runs as a promise reaction on whichever thread drains
    // the loop, and the CLR side only provides synchronous helpers: the handler snapshot,
    // the delivery counter, the stop flag.
    public JsValue publish => _publishJs ??= BuildPublishFunction();

    /// <summary>
    /// The topic's delivery loop. <c>begin</c> snapshots the subscribers and mints the event
    /// (<c>null</c> when nobody listens — the classic pub/sub loss, chapter 06); the loop then
    /// hands the event to each handler, sequentially with <c>stopPropagation()</c> /
    /// <c>markHandled()</c> short-circuiting the chain, or all at once under
    /// <c>Promise.all</c>, where the parallel mode's <c>handlerCount</c> advances as each
    /// handler is started. A handler that throws or rejects rejects publish.
    /// </summary>
    internal const string PublishFactorySource = """
        (parallel, begin, handlersOf, setHandlerCount, shouldStop) => async function publish(value) {
            const ev = begin(value);
            if (ev === null) return;
            const handlers = handlersOf(ev);
            if (parallel) {
                const pending = [];
                for (let i = 0; i < handlers.length; i++) {
                    setHandlerCount(ev, i + 1);
                    pending.push(handlers[i](ev));
                }
                await Promise.all(pending);
                return;
            }
            for (let i = 0; i < handlers.length; i++) {
                setHandlerCount(ev, i + 1);
                await handlers[i](ev);
                if (shouldStop(ev)) break;
            }
        }
        """;

    private JsValue BuildPublishFunction()
    {
        // None of these helpers has a failure path of its own, so none needs the JsHostError
        // bridge: they read and write plain fields of an event this topic minted.
        Func<JsValue, JsPublishedEvent?> begin = value =>
        {
            JsValue[] snapshot;
            lock (_lock) snapshot = _handlers.Select(h => h.Handler).ToArray();
            return snapshot.Length == 0
                ? null
                : new JsPublishedEvent(_engine, value, snapshot.Length, _eventLocks)
                {
                    Handlers = snapshot,
                    // The body publishing the event is the one whose cancellation must release a
                    // handler queued on `ev.lock` — the publish promise is part of that body.
                    Cancellation = Broker?.CurrentCt ?? CancellationToken.None,
                };
        };
        Func<JsPublishedEvent, JsValue[]> handlersOf = ev => ev.Handlers;
        Action<JsPublishedEvent, int> setHandlerCount = (ev, count) => ev.handlerCount = count;
        Func<JsPublishedEvent, bool> shouldStop = ev => ev.ShouldStop;

        var factory = JsTrampolineFactories.Publish.For(_engine);
        return _engine.Invoke(factory, [_parallel, begin, handlersOf, setHandlerCount, shouldStop]);
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
