using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Doubles;

/// <summary>
/// Queues every dispatched action instead of running it, the way <c>Application.Invoke</c> defers
/// work to the next main-loop iteration. <see cref="InvokeCount"/> counts the dispatches;
/// <see cref="RunPending"/> plays the queue as one loop iteration would.
/// </summary>
internal sealed class FakeUiDispatcher : IUiDispatcher
{
    private readonly Lock _gate = new();
    private readonly Queue<Action> _queue = new();

    public int InvokeCount;

    public int PendingCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    public void Invoke(Action action)
    {
        lock (_gate)
        {
            InvokeCount++;
            _queue.Enqueue(action);
        }
    }

    /// <summary>Runs the actions queued so far (not those they queue themselves).</summary>
    public void RunPending()
    {
        Action[] batch;
        lock (_gate)
        {
            batch = [.. _queue];
            _queue.Clear();
        }
        foreach (var action in batch) action();
    }
}
