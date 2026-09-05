using Jint;
using Jint.Native;

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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks;

    public JsValue value { get; }
    public int handlerCount { get; internal set; }
    public int maxHandlers { get; }

    internal bool ShouldStop => _stopPropagation || _handled;

    internal JsPublishedEvent(
        Engine engine,
        JsValue value,
        int maxHandlers,
        System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> locks)
    {
        _engine = engine;
        this.value = value;
        this.maxHandlers = maxHandlers;
        _locks = locks;
    }

    public void markHandled() => _handled = true;
    public void stopPropagation() => _stopPropagation = true;

    public Func<string, JsValue, Task<JsValue>> @lock => async (lockName, fn) =>
    {
        var sem = _locks.GetOrAdd(lockName, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            var result = _engine.Invoke(fn, Array.Empty<object>());
            return result.IsPromise()
                ? await result.UnwrapIfPromiseAsync(CancellationToken.None).ConfigureAwait(false)
                : result;
        }
        finally
        {
            sem.Release();
        }
    };
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
