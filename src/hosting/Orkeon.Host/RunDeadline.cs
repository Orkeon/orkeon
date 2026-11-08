using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Host;

/// <summary>
/// The run timeout of one hosted run (FWK final review H1): <c>Orkeon:Host:RunTimeout</c> counts the time the run works,
/// not the time it waits for a person. While the run holds on a question (<see cref="IPendingInputBroker"/>) the clock
/// stops, so the hold timeout alone ends the wait (and switches the run to suspend); the remaining budget resumes when
/// the answer arrives.
/// </summary>
internal sealed class RunDeadline : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly IDisposable? _watch;
    private TimeSpan _remaining;
    private long _armedAt;
    private bool _paused;
    private bool _disposed;

    /// <summary>Arms the deadline.</summary>
    /// <param name="time">The clock.</param>
    /// <param name="timeout">The work budget of the run.</param>
    /// <param name="broker">Where the run's holds are seen; null when the host has no human input.</param>
    /// <param name="runId">The run, which is the session id its questions carry.</param>
    /// <param name="onExpired">Called once the budget is spent.</param>
    public RunDeadline(TimeProvider time, TimeSpan timeout, IPendingInputBroker? broker, string runId, Action onExpired)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(onExpired);
        _time = time;
        _remaining = timeout;
        _armedAt = time.GetTimestamp();
        _timer = time.CreateTimer(static state => ((Action)state!)(), onExpired, timeout, Timeout.InfiniteTimeSpan);
        _watch = broker?.WatchHolds(runId, SetHeld);
    }

    private void SetHeld(bool held)
    {
        lock (_gate)
        {
            if (_disposed || held == _paused)
                return;
            _paused = held;
            if (held)
            {
                _remaining -= _time.GetElapsedTime(_armedAt);
                if (_remaining < TimeSpan.Zero)
                    _remaining = TimeSpan.Zero;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _armedAt = _time.GetTimestamp();
                _timer.Change(_remaining, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _watch?.Dispose();
        _timer.Dispose();
    }
}
