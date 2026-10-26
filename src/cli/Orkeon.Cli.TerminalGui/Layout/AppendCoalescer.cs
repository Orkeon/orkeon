namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Coalesces items posted from any thread into one UI dispatch per burst (issue #196). Items are
/// kept in a thread-safe buffer; at most one <see cref="IUiDispatcher.Invoke"/> is in flight at a
/// time, and that dispatch drains every item buffered when it runs, in posting order. A burst of
/// lines written between two frames therefore costs one dispatch, not one per line.
/// </summary>
/// <remarks>UI-agnostic: it only knows the dispatcher and the action that applies a batch.</remarks>
internal sealed class AppendCoalescer<T>
{
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<IReadOnlyList<T>> _apply;
    private readonly Lock _gate = new();
    private readonly Action _drain;
    private List<T> _pending = [];
    private bool _scheduled;

    public AppendCoalescer(IUiDispatcher dispatcher, Action<IReadOnlyList<T>> apply)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(apply);
        _dispatcher = dispatcher;
        _apply = apply;
        _drain = Drain;
    }

    /// <summary>Number of items buffered and not yet applied.</summary>
    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    /// <summary>
    /// Buffers <paramref name="item"/> and schedules a drain unless one is already scheduled.
    /// The dispatcher is called outside the buffer's lock.
    /// </summary>
    public void Post(T item)
    {
        bool schedule;
        lock (_gate)
        {
            _pending.Add(item);
            schedule = !_scheduled;
            _scheduled = true;
        }
        if (schedule) _dispatcher.Invoke(_drain);
    }

    private void Drain()
    {
        List<T> batch;
        lock (_gate)
        {
            batch = _pending;
            _pending = [];
            _scheduled = false;
        }
        if (batch.Count > 0) _apply(batch);
    }
}
