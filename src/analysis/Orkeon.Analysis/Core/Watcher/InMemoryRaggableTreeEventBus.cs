using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core.Watcher;

public sealed class InMemoryRaggableTreeEventBus : IRaggableTreeEventBus
{
    private readonly ConcurrentDictionary<Guid, Func<RaggableTreeUpdated, CancellationToken, Task>> _handlers = new();

    public void Publish(RaggableTreeUpdated evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        foreach (var handler in _handlers.Values)
        {
            _ = InvokeAsync(handler, evt);
        }
    }

    public IDisposable Subscribe(Func<RaggableTreeUpdated, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = Guid.NewGuid();
        _handlers[id] = handler;
        return new Subscription(this, id);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget dispatch: a faulty subscriber must not break the bus or other handlers.")]
    private static async Task InvokeAsync(
        Func<RaggableTreeUpdated, CancellationToken, Task> handler,
        RaggableTreeUpdated evt)
    {
        try { await handler(evt, CancellationToken.None).ConfigureAwait(false); }
        catch { /* fire-and-forget dispatch: a faulty subscriber must not break the bus or other handlers */ }
    }

    private sealed class Subscription(InMemoryRaggableTreeEventBus bus, Guid id) : IDisposable
    {
        public void Dispose() => bus._handlers.TryRemove(id, out _);
    }
}
