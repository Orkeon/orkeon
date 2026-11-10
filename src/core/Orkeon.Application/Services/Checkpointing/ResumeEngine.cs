using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Checkpointing;
#pragma warning disable ORKEXP007 // The run history is experimental; the engine reads it to tell a live run from a dead one.
using Orkeon.Application.Runs;

namespace Orkeon.Application.Services.Checkpointing;

/// <summary>
/// Engine for determining whether and how a crew execution can be resumed from a checkpoint.
/// </summary>
public sealed partial class ResumeEngine : IResumeEngine
{
    private readonly ICheckpointManager _checkpointManager;
    private readonly IStateStore _store;
    private readonly ILogger<ResumeEngine> _logger;
    private readonly IRunHistoryReader? _history;
    private readonly IRunProcessProbe _probe;
    private readonly string _localHost;

    /// <summary>
    /// Initializes a new instance of <see cref="ResumeEngine"/>.
    /// </summary>
    public ResumeEngine(
        ICheckpointManager checkpointManager,
        IStateStore store,
        ILogger<ResumeEngine> logger,
        IRunHistoryReader? history = null,
        IRunProcessProbe? probe = null)
    {
        _history = history;
        _probe = probe ?? new SystemRunProcessProbe();
        _localHost = RunProcessIdentity.Current.Host;
        ArgumentNullException.ThrowIfNull(checkpointManager);
        _checkpointManager = checkpointManager;
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<bool> CanResumeAsync(string crewKey, CancellationToken ct = default)
    {
        var latest = await _checkpointManager.GetLatestCheckpointAsync(crewKey, ct).ConfigureAwait(false);
        if (latest == null)
            return false;

        // Can resume if session is not completed (i.e., it's running, failed, or suspended)
        return latest.Phase != SessionPhase.Completed;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<ResumeResult> ResumeAsync(string crewKey, ResumeOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ResumeOptions();

        var located = !string.IsNullOrEmpty(options.ExecutionId)
            ? await LocateByExecutionIdAsync(crewKey, options.ExecutionId, ct).ConfigureAwait(false)
            : !string.IsNullOrEmpty(options.FromVersionId)
                ? await LocateByVersionAsync(crewKey, options.FromVersionId, ct).ConfigureAwait(false)
                : await LocateLatestUnfinishedAsync(crewKey, ct).ConfigureAwait(false);
        if (located.Early is not null)
            return located.Early;

        var latest = located.State;
        if (latest == null)
        {
            LogNoCheckpointFound(crewKey);
            return new ResumeResult();
        }

        // A completed run has nothing left to do.
        if (latest.Phase == SessionPhase.Completed)
        {
            LogNothingToResume(crewKey, latest.SessionId);
            return new ResumeResult();
        }

        var completedIds = new List<string>();
        var completedOutputs = new Dictionary<string, string?>();
        var skipCount = 0;

        foreach (var (taskId, checkpoint) in latest.TaskCheckpoints)
        {
            switch (checkpoint.Status)
            {
                case CheckpointStatus.Completed:
                    completedIds.Add(taskId);
                    completedOutputs[taskId] = checkpoint.Output;
                    break;

                case CheckpointStatus.Failed:
                    if (options.SkipFailed)
                    {
                        latest = await SkipFailedTaskAsync(latest, taskId, checkpoint, ct).ConfigureAwait(false);
                        skipCount++;
                    }
                    else if (options.RetryFailed && checkpoint.RetryCount < options.MaxRetries)
                    {
                        // Task will be retried: it stays out of the completed outputs
                        LogTaskWillBeRetried(taskId, checkpoint.RetryCount + 1, options.MaxRetries);
                    }

                    // Otherwise it cannot be retried and is not skipped: just advance past it
                    break;

                case CheckpointStatus.Skipped:
                    skipCount++;
                    break;

                case CheckpointStatus.Pending:
                default:
                    // Pending tasks should be re-executed
                    break;
            }
        }

        LogResumeResult(crewKey, completedIds.Count, skipCount);

        return new ResumeResult
        {
            SessionId = latest.SessionId,
            SkipCount = skipCount,
            CompletedTaskIds = completedIds,
            CompletedOutputs = completedOutputs,
        };
    }

    // The state found for a resume, or the result that ends it early (nothing found, or a live run).
    private readonly record struct Located(SessionState? State, ResumeResult? Early);

    // Mark as skipped: update the checkpoint status in the store.
    private async System.Threading.Tasks.Task<SessionState> SkipFailedTaskAsync(
        SessionState latest, string taskId, TaskCheckpoint checkpoint, CancellationToken ct)
    {
        var checkpoints = new Dictionary<string, TaskCheckpoint>(latest.TaskCheckpoints)
        {
            [taskId] = checkpoint with { Status = CheckpointStatus.Skipped }
        };
        var updated = latest with { TaskCheckpoints = checkpoints, UpdatedAt = DateTime.UtcNow };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        LogSkippedFailedTask(taskId, updated.SessionId);
        return updated;
    }

    // A session the caller names is the one resumed, when it is this crew's: another crew's state is
    // never handed to a run that happens to be given its id.
    private async System.Threading.Tasks.Task<Located> LocateByExecutionIdAsync(string crewKey, string executionId, CancellationToken ct)
    {
        var latest = await _store.GetAsync(executionId, ct).ConfigureAwait(false);
        if (latest is null || !string.Equals(latest.CrewKey, crewKey, StringComparison.Ordinal))
        {
            LogNoCheckpointFound(crewKey);
            return new Located(null, new ResumeResult());
        }

        // A session another process still drives (a daemon that holds a run on its question) is not taken over, named or not.
        if (latest.Phase is SessionPhase.Running or SessionPhase.WaitingForInput or SessionPhase.Failed
            && await ProvenLiveAsync(latest.SessionId, ct).ConfigureAwait(false))
        {
            LogRunningNotResumed(crewKey, latest.SessionId);
            return new Located(null, new ResumeResult { Refusal = LiveRefusal(latest.SessionId, latest.Phase) });
        }

        return new Located(latest, null);
    }

    // If a specific version is requested, restore from that version.
    private async System.Threading.Tasks.Task<Located> LocateByVersionAsync(string crewKey, string versionId, CancellationToken ct)
    {
        var checkpoint = await _checkpointManager.GetLatestCheckpointAsync(crewKey, ct).ConfigureAwait(false);
        var restored = checkpoint == null
            ? null
            : await _checkpointManager.RestoreToVersionAsync(checkpoint.SessionId, versionId, ct).ConfigureAwait(false);
        if (restored == null)
        {
            LogNoCheckpointFound(crewKey);
            return new Located(null, new ResumeResult());
        }

        return new Located(restored, null);
    }

    // `latest` is the newest run of this crew that is not completed: a newer completed run does not
    // hide an older failed one. A run still marked Running may be alive in another process (the
    // store holds no lease), so it is resumed only when its id is given.
    private async System.Threading.Tasks.Task<Located> LocateLatestUnfinishedAsync(string crewKey, CancellationToken ct)
    {
        var unfinished = (await _store.ListAsync(ct).ConfigureAwait(false))
            .Where(s => string.Equals(s.CrewKey, crewKey, StringComparison.Ordinal) && s.Phase != SessionPhase.Completed)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefault();
        if (unfinished is null)
        {
            LogNoCheckpointFound(crewKey);
            return new Located(null, new ResumeResult());
        }

        if (unfinished.Phase == SessionPhase.Running
            || (unfinished.Phase is SessionPhase.WaitingForInput or SessionPhase.Failed && await ProvenLiveAsync(unfinished.SessionId, ct).ConfigureAwait(false)))
        {
            LogRunningNotResumed(crewKey, unfinished.SessionId);
            return new Located(null, new ResumeResult { Refusal = LiveRefusal(unfinished.SessionId, unfinished.Phase) });
        }

        return new Located(await _store.GetAsync(unfinished.SessionId, ct).ConfigureAwait(false), null);
    }

    private static string LiveRefusal(string sessionId, SessionPhase phase) =>
        $"Run {sessionId} is still marked {phase}: it may be alive in another process, and 'latest' does not take a run over from it. " +
        $"If it is dead (killed, crashed), resume it by its id: --resume {sessionId}.";

    // The run history says what the session cannot: a run recorded as Running whose process is alive on this host (or that ran on another host) is live.
    // A session whose run left no record (history disabled) cannot be proven live: a run that stopped on a question and exited is resumable by "latest".
    private async System.Threading.Tasks.Task<bool> ProvenLiveAsync(string sessionId, CancellationToken ct)
    {
        if (_history is null || await _history.GetAsync(sessionId, ct).ConfigureAwait(false) is not { } summary)
            return false;

        return RunLiveness.Apply(summary, _probe, _localHost).Status == RunStatus.Running;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The latest unfinished run {SessionId} of crew {CrewKey} is still marked Running: not resumed without its id")]
    private partial void LogRunningNotResumed(string crewKey, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} of crew {CrewKey} is completed, nothing to resume")]
    private partial void LogNothingToResume(string crewKey, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No checkpoint found for crew {CrewKey}, cannot resume")]
    private partial void LogNoCheckpointFound(string crewKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped failed task {TaskId} in session {SessionId}")]
    private partial void LogSkippedFailedTask(string taskId, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} will be retried (attempt {Attempt}/{Max})")]
    private partial void LogTaskWillBeRetried(string taskId, int attempt, int max);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resume result for crew {CrewKey}: {CompletedCount} completed, {SkipCount} skipped")]
    private partial void LogResumeResult(string crewKey, int completedCount, int skipCount);
}
