namespace Orkeon.Tests.Shared.Timing;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when the test calls <see cref="Advance"/>:
/// the timers due by then fire, timestamps follow. <c>Task.Delay(TimeSpan, TimeProvider,
/// CancellationToken)</c> creates one of its timers, so a wait of a minute is exercised without
/// waiting a minute. The shape of the clock of <c>Orkeon.Studio.Core.Tests</c>, with periodic timers
/// and <see cref="PendingTimers"/>, which tells a test that something is waiting on the clock.
/// </summary>
/// <remarks>
/// A timer fires on the thread that advances the clock, outside the clock's lock: what it resumes
/// may read the clock or arm another timer. A wait released by <see cref="Advance"/> resumes
/// asynchronously — await the task of the call before asserting what it did.
/// </remarks>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now;

    /// <summary>A clock set at <paramref name="start"/>; 2026-10-01 09:00 UTC when none is given.</summary>
    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        Start = start ?? new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        _now = Start;
    }

    /// <summary>The instant the clock was created at.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>How far the clock moved since it was created.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
                return _now - Start;
        }
    }

    /// <summary>The timers armed and not yet due: a <c>Task.Delay</c> on this clock in progress is one.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_gate)
                return _timers.Count(timer => timer.DueAt is not null);
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and fires, in order, every timer that falls due on the way.</summary>
    /// <param name="by">How far to move; zero fires what is due now.</param>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        DateTimeOffset target;
        lock (_gate)
            target = _now + by;

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers
                    .Where(timer => timer.DueAt is { } at && at <= target)
                    .OrderBy(timer => timer.DueAt)
                    .FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.DueAt!.Value;
                next.DueAt = next.Period is { } period ? _now + period : null;
            }

            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan? Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period;
                if (!owner._timers.Contains(this))
                    owner._timers.Add(this);
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
                owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
