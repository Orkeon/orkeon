using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Event handed to topic subscribers. Tracks delivery position
/// (<see cref="handlerCount"/> / <see cref="maxHandlers"/>) and exposes
/// <c>markHandled()</c> / <c>stopPropagation()</c> / <c>lock(name, fn)</c>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of PublishedEvent in Typings/events.d.ts; that declaration is the contract scripts read.
public sealed class JsPublishedEvent
{
    private readonly Engine _engine;
    private bool _stopPropagation;
    private bool _handled;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
    private JsValue? _lockJs;

    public JsValue value { get; }
    public int handlerCount { get; internal set; }
    public int maxHandlers { get; }

    internal bool ShouldStop => _stopPropagation || _handled;

    /// <summary>
    /// The subscribers this event is delivered to, frozen by <see cref="JsEventTopic.publish"/>
    /// at publish time so that a handler subscribing or unsubscribing mid-delivery does not
    /// alter the current round.
    /// </summary>
    internal JsValue[] Handlers { get; init; } = [];

    /// <summary>
    /// The token of the body that published this event: a handler queued on
    /// <c>lock(name, fn)</c> is released — its promise rejected — when that body is cancelled,
    /// exactly as <c>ctx.lock</c> honours <c>ctx.signal</c>.
    /// </summary>
    internal CancellationToken Cancellation { get; init; } = CancellationToken.None;

    internal JsPublishedEvent(
        Engine engine,
        JsValue value,
        int maxHandlers,
        ConcurrentDictionary<string, SemaphoreSlim> locks)
    {
        _engine = engine;
        this.value = value;
        this.maxHandlers = maxHandlers;
        _locks = locks;
    }

    public void markHandled() => _handled = true;
    public void stopPropagation() => _stopPropagation = true;

    // The shared named-lock trampoline (JsTrampolineFactories.Lock). The locks are the
    // topic's, shared by every event it publishes, so two parallel handlers of the same event
    // serialize on a name exactly as chapter 06 promises. A cancelled wait is a cancelled
    // Task: Jint rejects the acquire promise on the loop, so the waiter unwinds as a JS
    // rejection and never invokes its callback.
    public JsValue @lock => _lockJs ??= BuildLockFunction();

    private JsValue BuildLockFunction()
    {
        Func<string, Task<JsValue>> acquire = async name =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var sem = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(Cancellation).ConfigureAwait(false);
            return JsValue.Undefined;
        };
        Action<string> release = name =>
        {
            if (_locks.TryGetValue(name, out var sem)) sem.Release();
        };
        return _engine.Invoke(JsTrampolineFactories.Lock.For(_engine), [acquire, release]);
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
