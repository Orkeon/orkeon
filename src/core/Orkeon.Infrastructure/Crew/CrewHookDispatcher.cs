using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Constants.Protocol;

namespace Orkeon.Infrastructure.Crew;

/// <summary>
/// Dispatches <see cref="ICrewExecutionHook"/> callbacks on behalf of an orchestration
/// strategy (BUS-03). Before this existed, only <c>SequentialProcessStrategy</c> notified
/// the hook — it carried the three fault-barriered helpers privately, and the five other
/// modes stayed silent, so anything observing a run saw nothing outside the sequential
/// path. Factoring the dispatch out is what lets every mode emit the same events without
/// six copies of the same try/catch.
/// <para>
/// <b>Every dispatch is best-effort.</b> A faulty hook is logged and swallowed: observing a
/// run must never change its outcome. That rule is the reason this type exists rather than
/// a plain interface call at each site.
/// </para>
/// <para>
/// It is also where a streamed run hears its tasks (GAP-32): each start, end and failure is written
/// to the run's stream (<see cref="CrewStreamScope"/>) as <c>task.started</c>, <c>task.completed</c>
/// and <c>error</c>, before the hook is called — the hook the host registered is served as before.
/// And it marks the run's end as reported (<see cref="CrewRunEnding"/>), so the orchestrator reports
/// only a failure no strategy did.
/// </para>
/// </summary>
internal sealed partial class CrewHookDispatcher
{
    private readonly ICrewExecutionHook? _hook;
    private readonly ILogger _logger;

    /// <summary>Creates a dispatcher; a null hook makes every call a no-op.</summary>
    internal CrewHookDispatcher(ICrewExecutionHook? hook, ILogger? logger = null)
    {
        _hook = hook;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>
    /// Whether anything is listening — the hook, or the stream of a streamed run — lets a caller
    /// skip building a snapshot for nobody.
    /// </summary>
    internal bool HasHook => _hook is not null || CrewStreamScope.IsOpen;

    /// <summary>
    /// Notifies that one task is starting (STUDIO-17). Dispatched by every mode at the moment
    /// the task and its agent are both known and nothing has been asked yet — the point a
    /// watcher shows as "in progress" until the matching <see cref="TaskCompletedAsync"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort hook dispatch: a faulty start hook is logged and must not break the crew execution pipeline.")]
    internal async Task TaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CrewStreamScope.Write(new CrewExecutionEvent
        {
            Kind = RunEventKinds.TaskStarted,
            Timestamp = snapshot.StartedAt,
            TaskId = snapshot.TaskId,
            AgentRole = snapshot.AgentRole,
        });

        if (_hook is null) return;
        try
        {
            await _hook.OnTaskStartedAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTaskStartedFailed(ex);
        }
    }

    /// <summary>Notifies that one task finished, successfully or not.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort hook dispatch: a faulty completion hook is logged and must not break the crew execution pipeline.")]
    internal async Task TaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CrewStreamScope.Write(new CrewExecutionEvent
        {
            Kind = RunEventKinds.TaskCompleted,
            Timestamp = snapshot.CompletedAt,
            TaskId = snapshot.TaskId,
            AgentRole = snapshot.AgentRole,
            Success = snapshot.Success,
            Skipped = snapshot.Skipped,
            SkipReason = snapshot.SkipReason,
            Duration = snapshot.Duration,
            Tokens = snapshot.TokensUsed,
            ToolCalls = snapshot.ToolCallCount,
        });

        if (_hook is null) return;
        try
        {
            await _hook.OnTaskCompletedAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTaskCompletedFailed(ex);
        }
    }

    /// <summary>Notifies that the crew reached its end.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort hook dispatch: a faulty crew-completed hook is logged and must not break the crew execution pipeline.")]
    internal async Task CrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct = default)
    {
        CrewRunEnding.Record(failed: false);
        if (_hook is null) return;
        try
        {
            await _hook.OnCrewCompletedAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCrewCompletedFailed(ex);
        }
    }

    /// <summary>Notifies that the crew failed or was cancelled, with the cause when there is one.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort hook dispatch: a failure here is logged and must not mask the original failure being reported.")]
    internal async Task CrewFailedAsync(CrewExecutionSnapshot snapshot, Exception? cause, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CrewRunEnding.Record(failed: true);
        CrewStreamScope.Write(FailureEvent(snapshot.Status, snapshot.FailureReason ?? cause?.Message));

        if (_hook is null) return;
        try
        {
            await _hook.OnCrewFailedAsync(snapshot, cause, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCrewFailedFailed(ex);
        }
    }

    /// <summary>
    /// The <c>error</c> a stopped run ends on, before <c>run.finished</c>: <c>crew_cancelled</c> or
    /// <c>crew_failed</c>, with the run's reason — the code the wire carries (<c>--events</c>).
    /// </summary>
    internal static CrewExecutionEvent FailureEvent(CrewHookStatus status, string? reason) =>
        new()
        {
            Kind = RunEventKinds.Error,
            Code = status == CrewHookStatus.Canceled ? RunEventErrorCodes.CrewCancelled : RunEventErrorCodes.CrewFailed,
            Message = reason ?? string.Empty,
        };

    /// <summary>The start snapshot every mode reports the same way, stamped now.</summary>
    internal static TaskStartSnapshot Started(string taskId, string agentRole) =>
        new()
        {
            TaskId = taskId,
            AgentRole = agentRole,
            StartedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Builds the crew-level snapshot every mode reports the same way.</summary>
    internal static CrewExecutionSnapshot Snapshot(
        string crewId,
        DateTimeOffset startedAt,
        IEnumerable<TaskExecutionSnapshot> tasks,
        CrewHookStatus status,
        string? failureReason = null) =>
        new()
        {
            CrewId = crewId,
            StartedAt = startedAt,
            EndedAt = DateTimeOffset.UtcNow,
            Tasks = tasks.ToImmutableList(),
            Status = status,
            FailureReason = failureReason,
        };

    [LoggerMessage(EventId = 9404, Level = LogLevel.Warning, Message = "ICrewExecutionHook.OnTaskStartedAsync threw; ignored.")]
    private partial void LogTaskStartedFailed(Exception ex);

    [LoggerMessage(EventId = 9401, Level = LogLevel.Warning, Message = "ICrewExecutionHook.OnTaskCompletedAsync threw; ignored.")]
    private partial void LogTaskCompletedFailed(Exception ex);

    [LoggerMessage(EventId = 9402, Level = LogLevel.Warning, Message = "ICrewExecutionHook.OnCrewCompletedAsync threw; ignored.")]
    private partial void LogCrewCompletedFailed(Exception ex);

    [LoggerMessage(EventId = 9403, Level = LogLevel.Warning, Message = "ICrewExecutionHook.OnCrewFailedAsync threw; ignored.")]
    private partial void LogCrewFailedFailed(Exception ex);
}
