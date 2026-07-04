using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Application.Services.Checkpointing;

/// <summary>
/// Engine for determining whether and how a crew execution can be resumed from a checkpoint.
/// </summary>
public sealed partial class ResumeEngine : IResumeEngine
{
    private readonly ICheckpointManager _checkpointManager;
    private readonly IStateStore _store;
    private readonly ILogger<ResumeEngine> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ResumeEngine"/>.
    /// </summary>
    public ResumeEngine(
        ICheckpointManager checkpointManager,
        IStateStore store,
        ILogger<ResumeEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(checkpointManager);
        _checkpointManager = checkpointManager;
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<bool> CanResumeAsync(string crewId, CancellationToken ct = default)
    {
        var latest = await _checkpointManager.GetLatestCheckpointAsync(crewId, ct).ConfigureAwait(false);
        if (latest == null)
            return false;

        // Can resume if session is not completed (i.e., it's running, failed, or suspended)
        return latest.Phase != SessionPhase.Completed;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<ResumeResult> ResumeAsync(string crewId, ResumeOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ResumeOptions();

        SessionState? latest;

        // If a specific version is requested, restore from that version
        if (!string.IsNullOrEmpty(options.FromVersionId))
        {
            var checkpoint = await _checkpointManager.GetLatestCheckpointAsync(crewId, ct).ConfigureAwait(false);
            if (checkpoint == null)
            {
                LogNoCheckpointFound(crewId);
                return new ResumeResult();
            }

            latest = await _checkpointManager.RestoreToVersionAsync(checkpoint.SessionId, options.FromVersionId, ct).ConfigureAwait(false);
            if (latest == null)
            {
                LogNoCheckpointFound(crewId);
                return new ResumeResult();
            }
        }
        else
        {
            latest = await _checkpointManager.GetLatestCheckpointAsync(crewId, ct).ConfigureAwait(false);
        }

        if (latest == null)
        {
            LogNoCheckpointFound(crewId);
            return new ResumeResult();
        }

        var completedIds = new List<string>();
        var completedOutputs = new Dictionary<string, string?>();
        var skipCount = 0;
        var resumeFromIndex = 0;

        foreach (var (taskId, checkpoint) in latest.TaskCheckpoints)
        {
            switch (checkpoint.Status)
            {
                case CheckpointStatus.Completed:
                    completedIds.Add(taskId);
                    completedOutputs[taskId] = checkpoint.Output;
                    resumeFromIndex++;
                    break;

                case CheckpointStatus.Failed:
                    if (options.SkipFailed)
                    {
                        // Mark as skipped: update the checkpoint status in the store
                        var checkpoints = new Dictionary<string, TaskCheckpoint>(latest.TaskCheckpoints)
                        {
                            [taskId] = checkpoint with { Status = CheckpointStatus.Skipped }
                        };
                        latest = latest with { TaskCheckpoints = checkpoints, UpdatedAt = DateTime.UtcNow };
                        await _store.SaveAsync(latest, ct).ConfigureAwait(false);

                        skipCount++;
                        resumeFromIndex++;
                        LogSkippedFailedTask(taskId, latest.SessionId);
                    }
                    else if (options.RetryFailed && checkpoint.RetryCount < options.MaxRetries)
                    {
                        // Task will be retried; do not advance resumeFromIndex
                        LogTaskWillBeRetried(taskId, checkpoint.RetryCount + 1, options.MaxRetries);
                    }
                    else
                    {
                        // Cannot retry and not skipping, just advance past it
                        resumeFromIndex++;
                    }
                    break;

                case CheckpointStatus.Skipped:
                    skipCount++;
                    resumeFromIndex++;
                    break;

                case CheckpointStatus.Pending:
                default:
                    // Pending tasks should be re-executed
                    break;
            }
        }

        LogResumeResult(crewId, completedIds.Count, skipCount, resumeFromIndex);

        return new ResumeResult
        {
            SessionId = latest.SessionId,
            SkipCount = skipCount,
            CompletedTaskIds = completedIds,
            CompletedOutputs = completedOutputs,
            ResumeFromIndex = resumeFromIndex
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No checkpoint found for crew {CrewId}, cannot resume")]
    private partial void LogNoCheckpointFound(string crewId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped failed task {TaskId} in session {SessionId}")]
    private partial void LogSkippedFailedTask(string taskId, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} will be retried (attempt {Attempt}/{Max})")]
    private partial void LogTaskWillBeRetried(string taskId, int attempt, int max);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resume result for crew {CrewId}: {CompletedCount} completed, {SkipCount} skipped, resume from index {Index}")]
    private partial void LogResumeResult(string crewId, int completedCount, int skipCount, int index);
}
