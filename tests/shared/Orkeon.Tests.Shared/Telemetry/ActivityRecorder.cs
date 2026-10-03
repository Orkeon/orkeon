using System.Diagnostics;

namespace Orkeon.Tests.Shared.Telemetry;

/// <summary>
/// Records the activities of named sources as they stop, for a test that asserts on spans
/// (GAP-41). An <see cref="ActivityListener"/> hears the whole process: the tests running
/// alongside stop activities on their own threads while this one reads. The recorder adds each
/// one under a lock and hands out copies only; and since it records the neighbours' spans too,
/// a test keeps to its own — the trace of a root it starts (<c>new Activity(…)</c>, then
/// <c>Start()</c>) when the code under test parents its spans on <see cref="Activity.Current"/>
/// (<see cref="Snapshot(ActivityTraceId)"/>), else a tag only it can carry.
/// </summary>
public sealed class ActivityRecorder : IDisposable
{
    private readonly List<Activity> _stopped = [];
    private readonly ActivityListener _listener;
    private bool _disposed;

    /// <summary>Starts recording the activities of the sources named <paramref name="sourceNames"/>.</summary>
    /// <param name="sourceNames">The <see cref="ActivitySource.Name"/>s to listen to.</param>
    public ActivityRecorder(params string[] sourceNames)
    {
        ArgumentNullException.ThrowIfNull(sourceNames);
        var sources = new HashSet<string>(sourceNames, StringComparer.Ordinal);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => sources.Contains(source.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Record,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>A copy of the activities recorded so far, in the order they stopped.</summary>
    public IReadOnlyList<Activity> Snapshot()
    {
        lock (_stopped)
            return [.. _stopped];
    }

    /// <summary>A copy of the activities of one trace — the spans under a root the test started.</summary>
    /// <param name="traceId">The trace of the root.</param>
    public IReadOnlyList<Activity> Snapshot(ActivityTraceId traceId)
    {
        lock (_stopped)
            return [.. _stopped.Where(activity => activity.TraceId == traceId)];
    }

    /// <summary>Stops listening: an activity that stops afterwards is not recorded.</summary>
    public void Dispose()
    {
        lock (_stopped)
            _disposed = true;
        _listener.Dispose();
    }

    private void Record(Activity activity)
    {
        lock (_stopped)
        {
            // A source may still hold the listener it read before Dispose detached it.
            if (!_disposed)
                _stopped.Add(activity);
        }
    }
}
