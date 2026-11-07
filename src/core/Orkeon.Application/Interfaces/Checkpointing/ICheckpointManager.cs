
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>
/// Manages session lifecycle and task-level checkpointing.
/// </summary>
public interface ICheckpointManager
{
    /// <summary>
    /// Starts a checkpoint session for one run of the specified crew. The session is the run: its
    /// identifier is the run's <see cref="ExecutionId"/>, never a second one.
    /// </summary>
    /// <param name="sessionId">The run's execution identifier.</param>
    /// <param name="crewKey">The stable key of the crew (its name and the virtual path of its file).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The session identifier: <paramref name="sessionId"/> in its string form.</returns>
    System.Threading.Tasks.Task<string> StartSessionAsync(ExecutionId sessionId, string crewKey, CancellationToken ct = default);

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
    /// Retrieves the latest checkpoint for a given crew key.
    /// </summary>
    System.Threading.Tasks.Task<SessionState?> GetLatestCheckpointAsync(string crewKey, CancellationToken ct = default);

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
