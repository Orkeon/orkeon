using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;

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

    /// <summary>Whether anything is listening — lets a caller skip building a snapshot for nobody.</summary>
    internal bool HasHook => _hook is not null;

    /// <summary>Notifies that one task finished, successfully or not.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort hook dispatch: a faulty completion hook is logged and must not break the crew execution pipeline.")]
    internal async Task TaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct = default)
    {
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

    [LoggerMessage(EventId = 9401, Level = LogLevel.Warning, Message = "ICrewExecutionHook.OnTaskCompletedAsync threw; ignored.")]
    private partial void LogTaskCompletedFailed(Exception ex);

    [LoggerMessage(EventId = 9402, Level = LogLevel.Warning, Message = "ICrewExecutionHook.OnCrewCompletedAsync threw; ignored.")]
    private partial void LogCrewCompletedFailed(Exception ex);

    [LoggerMessage(EventId = 9403, Level = LogLevel.Warning, Message = "ICrewExecutionHook.OnCrewFailedAsync threw; ignored.")]
    private partial void LogCrewFailedFailed(Exception ex);
}
