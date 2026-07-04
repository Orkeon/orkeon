
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Application.Services.Checkpointing;

/// <summary>
/// Manages session lifecycle and task-level checkpointing.
/// Uses an <see cref="IStateStore"/> to persist session states.
/// </summary>
public sealed partial class CheckpointManager : ICheckpointManager
{
    private readonly IStateStore _store;
    private readonly ILogger<CheckpointManager> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CheckpointManager"/>.
    /// </summary>
    public CheckpointManager(IStateStore store, ILogger<CheckpointManager> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<string> StartSessionAsync(string crewId, CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var state = new SessionState
        {
            SessionId = sessionId,
            CrewId = crewId,
            Phase = SessionPhase.Running,
            CompletedTaskIndex = 0,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(),
            CreatedAt = now,
            UpdatedAt = now
        };

        await _store.SaveAsync(state, ct).ConfigureAwait(false);
        LogSessionStarted(sessionId, crewId);
        return sessionId;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task CheckpointAsync(string sessionId, string taskId, string? output, CancellationToken ct = default)
    {
        var state = await _store.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (state == null)
        {
            LogSessionNotFoundForCheckpoint(sessionId);
            return;
        }

        var checkpoints = new Dictionary<string, TaskCheckpoint>(state.TaskCheckpoints)
        {
            [taskId] = new TaskCheckpoint
            {
                TaskId = taskId,
                Status = CheckpointStatus.Completed,
                Output = output,
                Timestamp = DateTime.UtcNow
            }
        };

        var updated = state with
        {
            CompletedTaskIndex = state.CompletedTaskIndex + 1,
            TaskCheckpoints = checkpoints,
            UpdatedAt = DateTime.UtcNow
        };

        // Try versioned save first; fall back to non-versioned for stores that don't support time travel
        try
        {
            await _store.SaveVersionedAsync(updated, stepId: taskId, label: null, ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        }

        LogTaskCheckpointed(taskId, sessionId);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task MarkFailedAsync(string sessionId, string taskId, Exception ex, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return MarkFailedCoreAsync();

        async System.Threading.Tasks.Task MarkFailedCoreAsync()
        {
            var state = await _store.GetAsync(sessionId, ct).ConfigureAwait(false);
            if (state == null)
            {
                LogSessionNotFoundForMarkFailed(sessionId);
                return;
            }

            var existingCheckpoint = state.TaskCheckpoints.TryGetValue(taskId, out var existing) ? existing : null;
            var retryCount = (existingCheckpoint?.RetryCount ?? 0) + 1;

            var checkpoints = new Dictionary<string, TaskCheckpoint>(state.TaskCheckpoints)
            {
                [taskId] = new TaskCheckpoint
                {
                    TaskId = taskId,
                    Status = CheckpointStatus.Failed,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    RetryCount = retryCount
                }
            };

            var updated = state with
            {
                Phase = SessionPhase.Failed,
                TaskCheckpoints = checkpoints,
                UpdatedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message
            };

            await _store.SaveAsync(updated, ct).ConfigureAwait(false);
            LogTaskMarkedFailed(taskId, sessionId, ex.Message);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task CompleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var state = await _store.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (state == null)
        {
            LogSessionNotFoundForComplete(sessionId);
            return;
        }

        var updated = state with
        {
            Phase = SessionPhase.Completed,
            UpdatedAt = DateTime.UtcNow
        };

        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        LogSessionCompleted(sessionId);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<SessionState?> GetLatestCheckpointAsync(string crewId, CancellationToken ct = default)
    {
        return _store.GetLatestForCrewAsync(crewId, ct);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default)
    {
        return _store.GetHistoryAsync(sessionId, limit, ct);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<SessionState?> RestoreToVersionAsync(string sessionId, string versionId, CancellationToken ct = default)
    {
        var versioned = await _store.GetVersionAsync(sessionId, versionId, ct).ConfigureAwait(false);
        if (versioned == null)
        {
            LogVersionNotFound(sessionId, versionId);
            return null;
        }

        // Overwrite the current session state with the restored version
        await _store.SaveAsync(versioned.State, ct).ConfigureAwait(false);
        LogVersionRestored(sessionId, versionId);
        return versioned.State;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<VersionedState> ForkFromAsync(string sessionId, string versionId, string? label = null, CancellationToken ct = default)
    {
        return _store.ForkAsync(sessionId, versionId, label, ct);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default)
    {
        return _store.DiffAsync(sessionId, fromVersionId, toVersionId, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Version {VersionId} not found for session {SessionId}")]
    private partial void LogVersionNotFound(string sessionId, string versionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restored session {SessionId} to version {VersionId}")]
    private partial void LogVersionRestored(string sessionId, string versionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started checkpoint session {SessionId} for crew {CrewId}")]
    private partial void LogSessionStarted(string sessionId, string crewId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot checkpoint: session {SessionId} not found")]
    private partial void LogSessionNotFoundForCheckpoint(string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checkpointed task {TaskId} in session {SessionId}")]
    private partial void LogTaskCheckpointed(string taskId, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot mark failed: session {SessionId} not found")]
    private partial void LogSessionNotFoundForMarkFailed(string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marked task {TaskId} as failed in session {SessionId}: {Error}")]
    private partial void LogTaskMarkedFailed(string taskId, string sessionId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot complete: session {SessionId} not found")]
    private partial void LogSessionNotFoundForComplete(string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed session {SessionId}")]
    private partial void LogSessionCompleted(string sessionId);
}
