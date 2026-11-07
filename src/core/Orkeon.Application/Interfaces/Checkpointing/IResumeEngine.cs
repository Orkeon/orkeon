namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>
/// Engine for determining whether and how a crew execution can be resumed from a checkpoint.
/// </summary>
public interface IResumeEngine
{
    /// <summary>
    /// Determines whether the specified crew has a resumable session.
    /// </summary>
    System.Threading.Tasks.Task<bool> CanResumeAsync(string crewKey, CancellationToken ct = default);

    /// <summary>
    /// Resumes from the latest checkpoint for the specified crew.
    /// </summary>
    System.Threading.Tasks.Task<ResumeResult> ResumeAsync(string crewKey, ResumeOptions? options = null, CancellationToken ct = default);
}

/// <summary>
/// Options for controlling resume behavior.
/// </summary>
public sealed record ResumeOptions
{
    /// <summary>Gets whether failed tasks should be retried.</summary>
    public bool RetryFailed { get; init; } = true;

    /// <summary>Gets the maximum number of retries for a failed task.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Gets whether failed tasks should be skipped instead of retried.</summary>
    public bool SkipFailed { get; init; }

    /// <summary>Gets the specific version identifier to resume from, if any.</summary>
    public string? FromVersionId { get; init; }

    /// <summary>
    /// Gets the session to resume, when the caller names one; null resumes the latest session of the
    /// crew. A session that is completed, that does not exist or that belongs to another crew is not
    /// resumed.
    /// </summary>
    public string? ExecutionId { get; init; }
}

/// <summary>
/// Result of a resume operation containing previously completed work.
/// </summary>
public sealed record ResumeResult
{
    /// <summary>
    /// Gets why a run that exists was not taken up, or null: the latest unfinished run is still marked
    /// <see cref="SessionPhase.Running"/> and its id was not named.
    /// </summary>
    public string? Refusal { get; init; }

    /// <summary>Gets the session identifier being resumed.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Gets the count of tasks that were skipped.</summary>
    public int SkipCount { get; init; }

    /// <summary>Gets the list of task identifiers that were already completed.</summary>
    public IReadOnlyList<string> CompletedTaskIds { get; init; } = Array.Empty<string>();

    /// <summary>Gets the outputs of completed tasks keyed by task identifier.</summary>
    public IReadOnlyDictionary<string, string?> CompletedOutputs { get; init; } = new Dictionary<string, string?>();
}
