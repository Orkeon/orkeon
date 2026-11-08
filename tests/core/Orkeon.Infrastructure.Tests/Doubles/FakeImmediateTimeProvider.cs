namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="TimeProvider"/> whose timers never wait: each one records the wait it was
/// asked for and fires at once, so a test reads the backoff a retry chose without sleeping it. With
/// <see cref="Hold"/> set the timers never fire, which leaves a wait in progress for a test that
/// cancels it; <see cref="TimerRequested"/> says when one began.
/// </summary>
public sealed class FakeImmediateTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<TimeSpan> _delays = [];

    /// <summary>Whether the timers stay pending instead of firing.</summary>
    public bool Hold { get; set; }

    /// <summary>The waits asked for, in order.</summary>
    public IReadOnlyList<TimeSpan> Delays
    {
        get { lock (_gate) return [.. _delays]; }
    }

    /// <summary>Completes when the first timer is created.</summary>
    public TaskCompletionSource TimerRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate) _delays.Add(dueTime);
        TimerRequested.TrySetResult();
        if (!Hold)
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new NoTimer();
    }

    private sealed class NoTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
