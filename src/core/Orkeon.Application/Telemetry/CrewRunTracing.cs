using System.Collections.Concurrent;
using System.Diagnostics;
using Orkeon.Constants.Llm;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Telemetry;

/// <summary>
/// Spans of a crew run: one <c>crew.run</c> and one <c>task.run</c> per task. The run's scope is
/// ambient (one per nested run, given back to the caller on dispose) and holds the run span and the
/// task spans. A task span is started without being current; only <see cref="EnterTask"/> makes it
/// current, for the flow that works on the task. Outside a run nothing is done; with no listener no span is
/// started, but the crew and its tasks are still counted on the <c>Orkeon</c> meter.
/// </summary>
public static class CrewRunTracing
{
    private static readonly AsyncLocal<RunScope?> Ambient = new();
    private static readonly NoScope Nothing = new();

    /// <summary>Opens the run's span, a child of the caller's current activity.</summary>
    /// <param name="crew">The crew that runs.</param>
    public static IDisposable Begin(Orkeon.Domain.Crew.Crew crew)
    {
        ArgumentNullException.ThrowIfNull(crew);
        var enclosing = Activity.Current;
        var span = OrkeonActivitySources.Crew.StartActivity(OrkeonRunSpans.CrewRun, ActivityKind.Internal);
        var process = crew.ProcessType.ToString();
        span?.SetTag(OrkeonRunSpans.CrewId, crew.Id.ToString());
        span?.SetTag(OrkeonRunSpans.CrewName, crew.Name);
        span?.SetTag(OrkeonRunSpans.CrewProcess, process);
        span?.SetTag(OrkeonRunSpans.CrewTaskCount, crew.Tasks.Count);
        var scope = new RunScope(span, enclosing, Ambient.Value, crew.Name ?? string.Empty, process);
        OrkeonMeter.CrewStarted(scope.CrewName, process);
        Ambient.Value = scope;
        return scope;
    }

    /// <summary>
    /// Marks the run in error, with its reason. The first reason stays. A reason that is a
    /// cancellation (<paramref name="errorType"/> names an <see cref="OperationCanceledException"/>)
    /// counts the run cancelled, not failed.
    /// </summary>
    public static void Fail(string reason, string errorType)
    {
        if (Ambient.Value is not { } scope || scope.Failed)
            return;

        scope.Failed = true;
        scope.Cancelled = errorType is "System.OperationCanceledException" or "System.Threading.Tasks.TaskCanceledException";
        scope.Span?.SetStatus(ActivityStatusCode.Error, reason);
        scope.Span?.SetTag(GenAiAttributes.ErrorType, errorType);
    }

    /// <summary>Starts a task span under the run's, without making it current.</summary>
    public static void StartTask(TaskId id, string? agentName)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (Ambient.Value is not { } scope)
            return;

        var current = Activity.Current;
        var span = scope.Span is null
            ? null
            : OrkeonActivitySources.Task.StartActivity(OrkeonRunSpans.TaskRun, ActivityKind.Internal, scope.Span.Context);
        Activity.Current = current;
        span?.SetTag(OrkeonRunSpans.TaskId, id.ToString());
        if (agentName is not null)
            span?.SetTag(GenAiAttributes.AgentName, agentName);
        if (scope.Tasks.TryAdd(id, new TaskSpan(span, scope.CrewName, agentName)))
            OrkeonMeter.TaskStarted(scope.CrewName, agentName);
    }

    /// <summary>
    /// Ends a task span at <paramref name="endedAt"/> (the execution's own end, when the record comes
    /// late), else now.
    /// </summary>
    public static void EndTask(TaskId id, TaskSpanStatus status, string? reason, DateTimeOffset? endedAt)
    {
        if (Ambient.Value is { } scope && scope.Tasks.TryRemove(id, out var task))
            task.End(status, reason, endedAt);
    }

    /// <summary>Records that the task goes to another agent.</summary>
    public static void HandOver(TaskId id, string toAgent)
    {
        if (Ambient.Value is { } scope && scope.Tasks.TryGetValue(id, out var task))
            task.Span?.AddEvent(new ActivityEvent(OrkeonRunSpans.HandOverEvent, tags: new ActivityTagsCollection { [OrkeonRunSpans.HandOverTo] = toAgent }));
    }

    /// <summary>Makes the task span current until the result is disposed, then gives the previous activity back.</summary>
    public static IDisposable EnterTask(TaskId id) =>
        Ambient.Value is { } scope && scope.Tasks.TryGetValue(id, out var task)
            ? new Entered(task)
            : Nothing;

    private sealed class NoScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class TaskSpan(Activity? span, string crewName, string? role)
    {
        private readonly long _startedAt = Stopwatch.GetTimestamp();

        public Activity? Span { get; } = span;

        public void End(TaskSpanStatus status, string? reason, DateTimeOffset? endedAt)
        {
            var outcome = status switch
            {
                TaskSpanStatus.Completed => "completed",
                TaskSpanStatus.Failed => "failed",
                _ => "cancelled",
            };
            OrkeonMeter.TaskEnded(crewName, role, outcome, Stopwatch.GetElapsedTime(_startedAt).TotalSeconds);
            if (Span is null)
                return;

            Span.SetTag(OrkeonRunSpans.TaskStatus, status switch
            {
                TaskSpanStatus.Completed => "completed",
                TaskSpanStatus.Failed => "failed",
                _ => "cancelled",
            });
            if (status != TaskSpanStatus.Completed)
            {
                Span.SetStatus(ActivityStatusCode.Error, reason);
                Span.SetTag(GenAiAttributes.ErrorType, status == TaskSpanStatus.Failed ? OrkeonRunSpans.ErrorTaskFailed : OrkeonRunSpans.ErrorTaskCancelled);
            }

            if (endedAt is { } end)
                Span.SetEndTime(end.UtcDateTime);
            var current = Activity.Current;
            Span.Stop();
            Activity.Current = current;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213", Justification = "The previous activity belongs to its own scope: this one gives it back as current, never disposes it.")]
    private sealed class Entered : IDisposable
    {
        private readonly Activity? _previous = Activity.Current;

        public Entered(TaskSpan task) => Activity.Current = task.Span;

        public void Dispose() => Activity.Current = _previous;
    }

    private sealed class RunScope(Activity? span, Activity? enclosing, RunScope? parent, string crewName, string process) : IDisposable
    {
        private readonly long _startedAt = Stopwatch.GetTimestamp();
        private bool _disposed;

        public Activity? Span { get; } = span;

        public string CrewName { get; } = crewName;

        public bool Failed { get; set; }

        public bool Cancelled { get; set; }

        public ConcurrentDictionary<TaskId, TaskSpan> Tasks { get; } = new();

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var id in Tasks.Keys)
            {
                if (Tasks.TryRemove(id, out var open))
                    open.End(TaskSpanStatus.Cancelled, "the run ended before the task did", endedAt: null);
            }

            var outcome = Cancelled ? "cancelled" : Failed ? "failed" : "completed";
            OrkeonMeter.CrewEnded(CrewName, process, outcome, Stopwatch.GetElapsedTime(_startedAt).TotalSeconds);
            Span?.Stop();
            Activity.Current = enclosing;
            Ambient.Value = parent;
        }
    }
}
