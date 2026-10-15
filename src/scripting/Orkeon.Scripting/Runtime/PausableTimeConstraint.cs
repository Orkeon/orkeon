using Jint;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The engine's wall-clock limit (<see cref="Configuration.ScriptingLimitsOptions.ExecutionTimeout"/>), measured
/// the way Jint's own <c>TimeoutInterval</c> constraint measures it — a window that opens at each
/// <see cref="Reset"/> and is checked between statements — except that it can be paused: the time the
/// permission gate takes to decide, an operator answering a tool-call approval, is not the script's.
/// </summary>
/// <remarks>
/// The clock belongs to the engine, so a pause stops it for everything that engine runs (every agent of a
/// crew, every run of a command file's engine). Pauses nest: the clock runs again when the last one ends.
/// </remarks>
internal sealed class PausableTimeConstraint : Constraint
{
    private readonly TimeSpan _limit;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private long _windowStart;
    private TimeSpan _paused;
    private int _pauses;
    private long _pausedSince;

    /// <summary>Creates the constraint; its window opens now, and again at each <see cref="Reset"/>.</summary>
    /// <param name="limit">The wall-clock time the engine may run, pauses excluded.</param>
    /// <param name="time">The clock; <see cref="TimeProvider.System"/> when null.</param>
    internal PausableTimeConstraint(TimeSpan limit, TimeProvider? time = null)
    {
        _limit = limit;
        _time = time ?? TimeProvider.System;
        _windowStart = _time.GetTimestamp();
    }

    /// <summary>Checked every few thousand statements, like Jint's time constraint.</summary>
    public override bool IsAmortizable => true;

    /// <summary>True while at least one pause is open.</summary>
    internal bool IsPaused
    {
        get
        {
            lock (_gate)
                return _pauses > 0;
        }
    }

    /// <inheritdoc />
    /// <exception cref="TimeoutException">The limit is spent, pauses excluded.</exception>
    public override void Check()
    {
        lock (_gate)
        {
            if (_pauses > 0 || _time.GetElapsedTime(_windowStart) - _paused <= _limit)
                return;
        }

        throw new TimeoutException();
    }

    /// <inheritdoc />
    public override void Reset()
    {
        lock (_gate)
        {
            _windowStart = _time.GetTimestamp();
            _paused = TimeSpan.Zero;
            if (_pauses > 0)
                _pausedSince = _windowStart;
        }
    }

    /// <summary>Stops the clock until the returned handle is disposed.</summary>
    internal IDisposable Pause()
    {
        lock (_gate)
        {
            if (_pauses++ == 0)
                _pausedSince = _time.GetTimestamp();
        }

        return new Resumption(this);
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (--_pauses == 0)
                _paused += _time.GetElapsedTime(_pausedSince);
        }
    }

    private sealed class Resumption(PausableTimeConstraint owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Resume();
        }
    }
}
