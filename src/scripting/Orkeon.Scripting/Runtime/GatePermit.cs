namespace Orkeon.Scripting.Runtime;

/// <summary>
/// One permit of the crew-wide concurrency gate, as the child that took it holds it. The permit can
/// be handed back while the child waits for its background children and taken again afterwards;
/// <see cref="Release"/> gives it back only if it is held, so a re-take cancelled mid-wait is not
/// handed back a second time by the owner's final release.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The take lock never exposes AvailableWaitHandle, so it owns no unmanaged resource to dispose.")]
internal sealed class GatePermit
{
    private readonly SemaphoreSlim _gate;
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _take = new(1, 1);
    private bool _held;
    private bool _closed;

    internal GatePermit(SemaphoreSlim gate)
    {
        _gate = gate;
    }

    /// <summary>True once the owner closed the permit.</summary>
    internal bool IsClosed { get { lock (_sync) return _closed; } }

    /// <summary>
    /// Takes the permit unless it is already held, cancelled by <paramref name="cancellationToken"/>; held
    /// only once the wait succeeded. Takes are serialised, so a concurrent second take waits for the first
    /// and finds the permit held instead of taking another slot.
    /// </summary>
    internal async Task AcquireAsync(CancellationToken cancellationToken)
    {
        await _take.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_closed) throw new OperationCanceledException("The permit's owner has ended.");
                if (_held) return;
            }
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool keep;
            lock (_sync) { keep = !_closed; _held = keep; }
            if (!keep)
            {
                _gate.Release();
                throw new OperationCanceledException("The permit's owner has ended.");
            }
        }
        finally { _take.Release(); }
    }

    /// <summary>
    /// The owner's final release: hands the permit back when held and closes it, so a re-take running or
    /// pending afterwards ends without holding a slot.
    /// </summary>
    internal void Close()
    {
        bool wasHeld;
        lock (_sync)
        {
            _closed = true;
            wasHeld = _held;
            _held = false;
        }
        if (wasHeld) _gate.Release();
    }

    /// <summary>Hands the permit back when held; a no-op otherwise.</summary>
    internal void Release()
    {
        lock (_sync)
        {
            if (!_held) return;
            _held = false;
        }
        _gate.Release();
    }
}
