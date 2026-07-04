
namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>
/// Manages session lifecycle and task-level checkpointing.
/// </summary>
public interface ICheckpointManager
{
    /// <summary>
    /// Starts a new checkpoint session for the specified crew.
    /// </summary>
    /// <returns>The unique session identifier.</returns>
    System.Threading.Tasks.Task<string> StartSessionAsync(string crewId, CancellationToken ct = default);

    /// <summary>
    /// Records a successful task checkpoint with its output.
    /// </summary>
    System.Threading.Tasks.Task CheckpointAsync(string sessionId, string taskId, string? output, CancellationToken ct = default);

    /// <summary>
    /// Records a task failure with exception details.
    /// </summary>
    System.Threading.Tasks.Task MarkFailedAsync(string sessionId, string taskId, Exception ex, CancellationToken ct = default);

    /// <summary>
    /// Marks the session as completed.
    /// </summary>
    System.Threading.Tasks.Task CompleteSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the latest checkpoint for a given crew.
    /// </summary>
    System.Threading.Tasks.Task<SessionState?> GetLatestCheckpointAsync(string crewId, CancellationToken ct = default);

    // ── Time-travel methods ──

    /// <summary>
    /// Returns the version history for a session, newest first.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// Restores the session to a specific version and returns the state.
    /// </summary>
    System.Threading.Tasks.Task<SessionState?> RestoreToVersionAsync(string sessionId, string versionId, CancellationToken ct = default);

    /// <summary>
    /// Forks a new session from a specific version of an existing session.
    /// </summary>
    System.Threading.Tasks.Task<VersionedState> ForkFromAsync(string sessionId, string versionId, string? label = null, CancellationToken ct = default);

    /// <summary>
    /// Computes the difference between two versions of a session.
    /// </summary>
    System.Threading.Tasks.Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default);
}
