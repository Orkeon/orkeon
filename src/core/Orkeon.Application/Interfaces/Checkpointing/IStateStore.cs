
namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>
/// Persistent store for session checkpoint states.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Saves a session state, creating or updating as needed.
    /// </summary>
    System.Threading.Tasks.Task SaveAsync(SessionState state, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a session state by its unique identifier.
    /// </summary>
    System.Threading.Tasks.Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the most recent session state for a given crew.
    /// </summary>
    System.Threading.Tasks.Task<SessionState?> GetLatestForCrewAsync(string crewId, CancellationToken ct = default);

    /// <summary>
    /// Lists summaries of all stored sessions.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes a session state by its unique identifier.
    /// </summary>
    System.Threading.Tasks.Task DeleteAsync(string sessionId, CancellationToken ct = default);

    // ── Time-travel methods (default implementations for backward compatibility) ──

    /// <summary>
    /// Saves a versioned snapshot of the session state (append-only).
    /// </summary>
    System.Threading.Tasks.Task<VersionedState> SaveVersionedAsync(SessionState state, string? stepId = null, string? label = null, CancellationToken ct = default)
        => throw new NotSupportedException("Time travel not supported by this store");

    /// <summary>
    /// Returns the version history for a session, newest first.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default)
        => throw new NotSupportedException("Time travel not supported by this store");

    /// <summary>
    /// Retrieves a specific versioned state by version identifier.
    /// </summary>
    System.Threading.Tasks.Task<VersionedState?> GetVersionAsync(string sessionId, string versionId, CancellationToken ct = default)
        => throw new NotSupportedException("Time travel not supported by this store");

    /// <summary>
    /// Retrieves the versioned state closest to the specified timestamp.
    /// </summary>
    System.Threading.Tasks.Task<VersionedState?> GetAtAsync(string sessionId, DateTime timestamp, CancellationToken ct = default)
        => throw new NotSupportedException("Time travel not supported by this store");

    /// <summary>
    /// Retrieves the versioned state associated with a specific step.
    /// </summary>
    System.Threading.Tasks.Task<VersionedState?> GetByStepAsync(string sessionId, string stepId, CancellationToken ct = default)
        => throw new NotSupportedException("Time travel not supported by this store");

    /// <summary>
    /// Forks a new session from a specific version of an existing session.
    /// </summary>
    System.Threading.Tasks.Task<VersionedState> ForkAsync(string sourceSessionId, string sourceVersionId, string? label = null, CancellationToken ct = default)
        => throw new NotSupportedException("Time travel not supported by this store");

    /// <summary>
    /// Computes the difference between two versions of a session.
    /// </summary>
    System.Threading.Tasks.Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default)
        => throw new NotSupportedException("Time travel not supported by this store");
}

/// <summary>
/// Full state of a checkpointed session.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724", Justification = "Conflicts only with the legacy System.Web.SessionState namespace, which is not referenced by this .NET 10 library; SessionState is established public API used across 20+ files in multiple projects.")]
public sealed record SessionState
{
    /// <summary>Gets the unique session identifier.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Gets the crew identifier this session belongs to.</summary>
    public string CrewId { get; init; } = "";

    /// <summary>Gets the current phase of the session.</summary>
    public SessionPhase Phase { get; init; } = SessionPhase.Pending;

    /// <summary>Gets the index of the last completed task.</summary>
    public int CompletedTaskIndex { get; init; }

    /// <summary>Gets the task-level checkpoint details keyed by task identifier.</summary>
    public IReadOnlyDictionary<string, TaskCheckpoint> TaskCheckpoints { get; init; } = new Dictionary<string, TaskCheckpoint>();

    /// <summary>Gets the UTC timestamp when the session was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets the UTC timestamp when the session was last updated.</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets an optional error message if the session failed.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Checkpoint state for an individual task within a session.
/// </summary>
public sealed record TaskCheckpoint
{
    /// <summary>Gets the task identifier.</summary>
    public string TaskId { get; init; } = "";

    /// <summary>Gets the status of this task checkpoint.</summary>
    public CheckpointStatus Status { get; init; } = CheckpointStatus.Pending;

    /// <summary>Gets the task output, if completed.</summary>
    public string? Output { get; init; }

    /// <summary>Gets the UTC timestamp of this checkpoint.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Gets the number of retry attempts for this task.</summary>
    public int RetryCount { get; init; }

    /// <summary>Gets an optional error message if the task failed.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Lightweight summary of a session for listing purposes.
/// </summary>
public sealed record SessionSummary
{
    /// <summary>Gets the unique session identifier.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Gets the crew identifier.</summary>
    public string CrewId { get; init; } = "";

    /// <summary>Gets the current phase of the session.</summary>
    public SessionPhase Phase { get; init; }

    /// <summary>Gets the count of completed tasks.</summary>
    public int CompletedTaskCount { get; init; }

    /// <summary>Gets the UTC timestamp when the session was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Gets the UTC timestamp when the session was last updated.</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Represents the lifecycle phase of a session.
/// </summary>
public enum SessionPhase
{
    /// <summary>Session has been created but not yet started.</summary>
    Pending,

    /// <summary>Session is actively executing.</summary>
    Running,

    /// <summary>Session completed successfully.</summary>
    Completed,

    /// <summary>Session failed during execution.</summary>
    Failed,

    /// <summary>Session was suspended and can be resumed.</summary>
    Suspended
}

/// <summary>
/// Represents the status of an individual task checkpoint.
/// </summary>
public enum CheckpointStatus
{
    /// <summary>Task has not started yet.</summary>
    Pending,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task execution failed.</summary>
    Failed,

    /// <summary>Task was skipped during resume.</summary>
    Skipped
}

/// <summary>
/// An immutable, versioned snapshot of a session state with metadata.
/// </summary>
public sealed record VersionedState
{
    /// <summary>Gets the unique version identifier.</summary>
    public string VersionId { get; init; } = "";

    /// <summary>Gets the session identifier this version belongs to.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Gets the monotonically increasing version number (1-based).</summary>
    public int Version { get; init; }

    /// <summary>Gets the full session state at this version.</summary>
    public SessionState State { get; init; } = new();

    /// <summary>Gets the optional step identifier associated with this version.</summary>
    public string? StepId { get; init; }

    /// <summary>Gets the optional human-readable label for this version.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the session identifier this version was forked from, if any.</summary>
    public string? ForkedFromSession { get; init; }

    /// <summary>Gets the version identifier this version was forked from, if any.</summary>
    public string? ForkedFromVersion { get; init; }

    /// <summary>Gets the UTC timestamp when this version was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Lightweight summary of a version for listing purposes.
/// </summary>
public sealed record VersionSummary
{
    /// <summary>Gets the unique version identifier.</summary>
    public string VersionId { get; init; } = "";

    /// <summary>Gets the session identifier.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Gets the version number.</summary>
    public int Version { get; init; }

    /// <summary>Gets the session phase at this version.</summary>
    public SessionPhase Phase { get; init; }

    /// <summary>Gets the count of completed tasks at this version.</summary>
    public int CompletedTaskCount { get; init; }

    /// <summary>Gets the optional step identifier.</summary>
    public string? StepId { get; init; }

    /// <summary>Gets the optional label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the UTC timestamp when this version was created.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Represents the difference between two versioned states.
/// </summary>
public sealed record StateDiff
{
    /// <summary>Gets the source (from) version identifier.</summary>
    public string FromVersionId { get; init; } = "";

    /// <summary>Gets the target (to) version identifier.</summary>
    public string ToVersionId { get; init; } = "";

    /// <summary>Gets whether the session phase changed.</summary>
    public bool PhaseChanged { get; init; }

    /// <summary>Gets the phase in the source version.</summary>
    public SessionPhase FromPhase { get; init; }

    /// <summary>Gets the phase in the target version.</summary>
    public SessionPhase ToPhase { get; init; }

    /// <summary>Gets the task-level differences.</summary>
    public IReadOnlyList<TaskDiff> TaskDiffs { get; init; } = Array.Empty<TaskDiff>();

    /// <summary>Gets the list of task identifiers added in the target version.</summary>
    public IReadOnlyList<string> AddedTaskIds { get; init; } = Array.Empty<string>();

    /// <summary>Gets the list of task identifiers removed in the target version.</summary>
    public IReadOnlyList<string> RemovedTaskIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents the difference for a single task between two versions.
/// </summary>
public sealed record TaskDiff
{
    /// <summary>Gets the task identifier.</summary>
    public string TaskId { get; init; } = "";

    /// <summary>Gets the status in the source version.</summary>
    public CheckpointStatus FromStatus { get; init; }

    /// <summary>Gets the status in the target version.</summary>
    public CheckpointStatus ToStatus { get; init; }

    /// <summary>Gets whether the task output changed.</summary>
    public bool OutputChanged { get; init; }
}
