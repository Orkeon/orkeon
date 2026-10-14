namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The time limit of the running task (CH-01). The orchestrator opens one around a task whose agent
/// declares a <c>MaxExecutionTime</c>; its <see cref="Token"/> falls when the limit elapses on the
/// given clock or when the caller cancels.
/// </summary>
/// <remarks>Backed by <see cref="AsyncLocal{T}"/>, like <see cref="CrewPlanScope"/>.</remarks>
public sealed class TaskDeadline : IDisposable
{
    private static readonly AsyncLocal<TaskDeadline?> Ambient = new();

    private readonly CancellationTokenSource _source;
    private readonly ITimer _timer;
    private readonly TaskDeadline? _enclosing;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private TimeSpan _remaining;
    private long _runningSince;
    private int _pauses;
    private bool _disposed;
    private volatile bool _expired;

    private TaskDeadline(TimeSpan limit, TimeProvider time, CancellationToken caller)
    {
        _enclosing = Ambient.Value;
        _time = time;
        _remaining = limit;
        _runningSince = time.GetTimestamp();
        _source = CancellationTokenSource.CreateLinkedTokenSource(caller);
        _timer = time.CreateTimer(static state => ((TaskDeadline)state!).Expire(), this, limit, Timeout.InfiniteTimeSpan);
        Ambient.Value = this;
    }

    /// <summary>The deadline of the running task, or null.</summary>
    public static TaskDeadline? Current => Ambient.Value;

    /// <summary>Token that falls when the limit elapses or the caller cancels.</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>True once the limit elapsed on the clock.</summary>
    public bool HasExpired => _expired;

    /// <summary>Opens a deadline for the running task; disposing it restores the enclosing one.</summary>
    public static TaskDeadline Begin(TimeSpan limit, TimeProvider time, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(time);
        return new TaskDeadline(limit, time, caller);
    }

    /// <summary>Suspends the countdown until the returned handle is disposed; pauses nest.</summary>
    public IDisposable Pause()
    {
        lock (_gate)
        {
            if (_pauses++ == 0 && !_disposed)
            {
                var left = _remaining - _time.GetElapsedTime(_runningSince);
                _remaining = left > TimeSpan.Zero ? left : TimeSpan.Zero;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        return new Resumption(this);
    }

    /// <summary>Pauses the current deadline and every enclosing one; a no-op handle when none is open.</summary>
    public static IDisposable PauseCurrent()
    {
        var handles = new List<IDisposable>();
        for (var deadline = Ambient.Value; deadline is not null; deadline = deadline._enclosing)
        {
            handles.Add(deadline.Pause());
        }

        return new Resumption(handles);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Ambient.Value = _enclosing;
        lock (_gate)
        {
            _disposed = true;
        }

        _timer.Dispose();
        _source.Dispose();
    }

    private void Expire()
    {
        _expired = true;
        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The task ended and the deadline was released while the timer fired: nothing to cancel.
        }
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (--_pauses == 0 && !_disposed && !_expired)
            {
                _runningSince = _time.GetTimestamp();
                _timer.Change(_remaining, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private sealed class Resumption : IDisposable
    {
        private readonly TaskDeadline? _deadline;
        private readonly List<IDisposable>? _handles;
        private int _done;

        public Resumption(TaskDeadline deadline) => _deadline = deadline;

        public Resumption(List<IDisposable> handles) => _handles = handles;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
            {
                return;
            }

            _deadline?.Resume();
            _handles?.ForEach(static h => h.Dispose());
        }
    }
}
