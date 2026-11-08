
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Application.Services.Checkpointing;

/// <summary>
/// Manages session lifecycle and task-level checkpointing.
/// Uses an <see cref="IStateStore"/> to persist session states.
/// </summary>
public sealed partial class CheckpointManager : ICheckpointManager, IPendingInputStore
{
    private readonly IStateStore _store;
    private readonly ILogger<CheckpointManager> _logger;

    // One lock per session: the tasks of a parallel wave checkpoint at the same time, and each write
    // reads the session, changes it and saves it whole, so two of them would lose one another's task.
    // An entry lives while a write holds or awaits it: the last one out removes it, so a session that
    // ended leaves nothing behind.
    private readonly Dictionary<string, SessionLock> _locks = new(StringComparer.Ordinal);

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
    public async System.Threading.Tasks.Task<string> StartSessionAsync(ExecutionId sessionId, string crewKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(crewKey);
        var id = sessionId.ToString();

        using (await EnterAsync(id, ct).ConfigureAwait(false))
        {
            var now = DateTime.UtcNow;
            var existing = await _store.GetAsync(id, ct).ConfigureAwait(false);

            // A resumed run keeps its execution id, so its session already exists: it continues, with the
            // checkpoints it holds, instead of starting over.
            var state = existing is null
                ? new SessionState
                {
                    SessionId = id,
                    CrewKey = crewKey,
                    Phase = SessionPhase.Running,
                    CompletedTaskIndex = 0,
                    TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(),
                    CreatedAt = now,
                    UpdatedAt = now
                }
                : existing with { Phase = SessionPhase.Running, ErrorMessage = null, UpdatedAt = now };

            await _store.SaveAsync(state, ct).ConfigureAwait(false);
            if (existing is null)
                LogSessionStarted(id, crewKey);
            else
                LogSessionContinued(id, crewKey, existing.TaskCheckpoints.Count);
        }

        return id;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task CheckpointAsync(string sessionId, string taskId, string? output, CancellationToken ct = default) =>
        CheckpointAsync(sessionId, taskId, output, 0, ct);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task CheckpointAsync(string sessionId, string taskId, string? output, int retryCount, CancellationToken ct = default)
    {
        using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
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
                Timestamp = DateTime.UtcNow,
                RetryCount = retryCount
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
            using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
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
        using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
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
    public System.Threading.Tasks.Task<SessionState?> GetLatestCheckpointAsync(string crewKey, CancellationToken ct = default)
    {
        return _store.GetLatestForCrewAsync(crewKey, ct);
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

    private async System.Threading.Tasks.Task<IDisposable> EnterAsync(string sessionId, CancellationToken ct)
    {
        SessionLock sessionLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(sessionId, out sessionLock!))
                _locks[sessionId] = sessionLock = new SessionLock();
            sessionLock.Users++;
        }

        try
        {
            await sessionLock.Gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Leave(sessionId, sessionLock);
            throw;
        }

        return new Release(this, sessionId, sessionLock);
    }

    private void Leave(string sessionId, SessionLock sessionLock)
    {
        lock (_locks)
        {
            if (--sessionLock.Users == 0)
            {
                _locks.Remove(sessionId);
                sessionLock.Gate.Dispose();
            }
        }
    }

    /// <summary>The number of sessions with a write in flight: zero once every run is over.</summary>
    internal int LockedSessions
    {
        get { lock (_locks) return _locks.Count; }
    }

    private sealed class SessionLock
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private sealed class Release(CheckpointManager owner, string sessionId, SessionLock sessionLock) : IDisposable
    {
        public void Dispose()
        {
            sessionLock.Gate.Release();
            owner.Leave(sessionId, sessionLock);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Continued checkpoint session {SessionId} for crew {CrewKey} with {Completed} checkpointed tasks")]
    private partial void LogSessionContinued(string sessionId, string crewKey, int completed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Version {VersionId} not found for session {SessionId}")]
    private partial void LogVersionNotFound(string sessionId, string versionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restored session {SessionId} to version {VersionId}")]
    private partial void LogVersionRestored(string sessionId, string versionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started checkpoint session {SessionId} for crew {CrewKey}")]
    private partial void LogSessionStarted(string sessionId, string crewKey);

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
