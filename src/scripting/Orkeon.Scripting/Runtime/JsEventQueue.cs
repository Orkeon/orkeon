using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Script-scoped point-to-point queue exposed to JS as <c>ctx.events.queue&lt;T&gt;(name)</c>.
/// Type-erased in V1 (holds <see cref="JsValue"/> instances directly).
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of EventQueue in Typings/events.d.ts; that declaration is the contract scripts read.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The type is the JS-surface point-to-point queue exposed to scripts as ctx.events.queue(name); the 'Queue' suffix mirrors the JS API name and accurately describes its FIFO semantics.")]
public sealed class JsEventQueue
{
    private readonly string _name;
    private readonly ConcurrentQueue<JsValue> _items = new();
    private readonly object _lock = new();
    private readonly List<TaskCompletionSource<JsValue>> _waiters = new();

    public string name => _name;

    internal JsEventQueue(string name) { _name = name; }

    public void push(JsValue item)
    {
        TaskCompletionSource<JsValue>? winner = null;
        lock (_lock)
        {
            if (_waiters.Count > 0)
            {
                winner = _waiters[0];
                _waiters.RemoveAt(0);
            }
            else
            {
                _items.Enqueue(item);
            }
        }
        winner?.TrySetResult(item);
    }

    public Func<JsValue?, Task<JsValue>> pop => async options =>
    {
        TimeSpan? timeout = null;
        if (options is not null && options.IsObject())
        {
            var to = options.Get("timeout");
            if (to.IsNumber()) timeout = TimeSpan.FromMilliseconds(to.AsNumber());
        }

        TaskCompletionSource<JsValue> tcs;
        lock (_lock)
        {
            if (_items.TryDequeue(out var existing)) return existing;
            tcs = new TaskCompletionSource<JsValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add(tcs);
        }

        if (timeout is null) return await tcs.Task.ConfigureAwait(false);

        using var cts = new CancellationTokenSource(timeout.Value);
        using var reg = cts.Token.Register(() =>
        {
            lock (_lock) { _waiters.Remove(tcs); }
            tcs.TrySetException(new ReceiveTimeoutException(timeout.Value));
        });
        return await tcs.Task.ConfigureAwait(false);
    };

    public JsValue peek() => _items.TryPeek(out var v) ? v : JsValue.Undefined;

    public int length => _items.Count;

    public object info() => new { length = _items.Count, waiters = _waiters.Count };

    public void kick()
    {
        TaskCompletionSource<JsValue>[] snapshot;
        lock (_lock)
        {
            snapshot = _waiters.ToArray();
            _waiters.Clear();
        }
        foreach (var w in snapshot) w.TrySetException(new WaiterKickedException(_name));
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
