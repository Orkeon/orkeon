using Orkeon.Domain.Common;

namespace Orkeon.Domain.Delegation;

/// <summary>
/// Represents a delegation request.
/// </summary>
public sealed record DelegationRequest
{
    /// <summary>Gets or sets the task identifier being delegated.</summary>
    public TaskId TaskId { get; init; } = TaskId.Create();
    /// <summary>Gets or sets the identifier of the agent delegating the task.</summary>
    public AgentId FromAgentId { get; init; } = AgentId.Create();
    /// <summary>Gets or sets the identifier of the agent receiving the delegation.</summary>
    public AgentId ToAgentId { get; init; } = AgentId.Create();
    /// <summary>Gets or sets the reason for delegation.</summary>
    public string Reason { get; init; } = string.Empty;
    /// <summary>Gets or sets the priority of the delegation request.</summary>
    public DelegationPriority Priority { get; init; } = DelegationPriority.Normal;
    /// <summary>Gets or sets additional context for the delegation.</summary>
    public Dictionary<string, object> Context { get; init; } = [];
    /// <summary>Gets or sets when the delegation was requested.</summary>
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Validates the delegation request invariants.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when FromAgentId equals ToAgentId or Reason is empty.</exception>
    public void Validate()
    {
        if (FromAgentId == ToAgentId)
            throw new InvalidOperationException("FromAgentId and ToAgentId must be different.");
        if (string.IsNullOrWhiteSpace(Reason))
            throw new InvalidOperationException("Reason must not be empty.");
    }

    /// <summary>
    /// Creates a validated <see cref="DelegationRequest"/> instance.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="fromAgentId">The delegating agent identifier.</param>
    /// <param name="toAgentId">The target agent identifier.</param>
    /// <param name="reason">The reason for delegation.</param>
    /// <param name="priority">The delegation priority.</param>
    /// <returns>A validated <see cref="DelegationRequest"/>.</returns>
    public static DelegationRequest CreateValidated(
        TaskId taskId,
        AgentId fromAgentId,
        AgentId toAgentId,
        string reason,
        DelegationPriority priority = DelegationPriority.Normal)
    {
        if (fromAgentId == toAgentId)
            throw new ArgumentException("FromAgentId and ToAgentId must be different.", nameof(toAgentId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason must not be empty.", nameof(reason));

        return new DelegationRequest
        {
            TaskId = taskId,
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Reason = reason,
            Priority = priority
        };
    }
}

/// <summary>
/// Represents the outcome of a delegation request (accept/reject).
/// Not to be confused with <see cref="Agent.DelegationDecision"/> which models an agent's delegation intent.
/// </summary>
public sealed record DelegationOutcome
{
    /// <summary>Gets or sets the request identifier this outcome responds to.</summary>
    public DelegationRequestId RequestId { get; init; } = DelegationRequestId.Create();
    /// <summary>Gets or sets whether the delegation was accepted.</summary>
    public bool Accepted { get; init; }
    /// <summary>Gets or sets the reason for the decision.</summary>
    public string? Reason { get; init; }
    /// <summary>Gets or sets an alternative agent identifier if the original was rejected.</summary>
    public AgentId? AlternativeAgentId { get; init; }
    /// <summary>Gets or sets when the decision was made.</summary>
    public DateTime DecidedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Delegation priority levels.
/// </summary>
public enum DelegationPriority
{
    /// <summary>Low priority delegation.</summary>
    Low,
    /// <summary>Normal priority delegation.</summary>
    Normal,
    /// <summary>High priority delegation.</summary>
    High,
    /// <summary>Critical priority delegation.</summary>
    Critical
}

/// <summary>
/// Represents agent skills for delegation matching.
/// </summary>
public sealed record AgentSkills
{
    /// <summary>Gets or sets the agent identifier.</summary>
    public AgentId AgentId { get; init; } = AgentId.Create();
    /// <summary>Gets or sets the list of skill names.</summary>
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    /// <summary>Gets or sets skill proficiency scores keyed by skill name.</summary>
    public Dictionary<string, double> SkillScores { get; init; } = [];
    /// <summary>Gets or sets the overall skill score.</summary>
    public double OverallScore { get; init; }
}

/// <summary>
/// Result of a delegation request.
/// </summary>
public sealed record DelegationResult
{
    /// <summary>Gets or sets the request identifier this result corresponds to.</summary>
    public DelegationRequestId RequestId { get; init; } = DelegationRequestId.Create();
    /// <summary>Gets or sets whether the delegation succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or sets the output produced by the delegated task.</summary>
    public string Output { get; init; } = string.Empty;
    /// <summary>Gets or sets the error message if the delegation failed.</summary>
    public string? Error { get; init; }
    /// <summary>Gets or sets when the delegation completed.</summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Gets or sets how long the delegation took.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Gets or sets additional metadata for the delegation result.</summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>Creates a successful delegation result.</summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="output">The output from the delegated task.</param>
    /// <returns>A successful <see cref="DelegationResult"/>.</returns>
    public static DelegationResult CreateSuccess(DelegationRequestId requestId, string output)
    {
        return new DelegationResult
        {
            RequestId = requestId,
            Success = true,
            Output = output
        };
    }

    /// <summary>Creates a failed delegation result.</summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="error">The error message.</param>
    /// <returns>A failed <see cref="DelegationResult"/>.</returns>
    public static DelegationResult CreateFailure(DelegationRequestId requestId, string error)
    {
        return new DelegationResult
        {
            RequestId = requestId,
            Success = false,
            Error = error
        };
    }
}

/// <summary>
/// Request for asking a question to another agent.
/// </summary>
public sealed record QuestionRequest
{
    /// <summary>Gets or sets the unique identifier for this question request.</summary>
    public QuestionRequestId Id { get; init; } = QuestionRequestId.Create();
    /// <summary>Gets or sets the identifier of the agent asking the question.</summary>
    public AgentId FromAgentId { get; init; } = AgentId.Create();
    /// <summary>Gets or sets the identifier of the agent being asked.</summary>
    public AgentId ToAgentId { get; init; } = AgentId.Create();
    /// <summary>Gets or sets the question text.</summary>
    public string Question { get; init; } = string.Empty;
    /// <summary>Gets or sets additional context for the question.</summary>
    public string Context { get; init; } = string.Empty;
    /// <summary>Gets or sets when the question was asked.</summary>
    public DateTime AskedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Gets or sets the timeout for the response, or <see langword="null"/> for no timeout.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// Information about an agent's capabilities.
/// </summary>
public sealed record AgentInfo
{
    private readonly double _availabilityScore = 1.0;

    /// <summary>Gets or sets the agent identifier.</summary>
    public AgentId Id { get; init; } = AgentId.Create();
    /// <summary>Gets or sets the agent role.</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>Gets or sets the list of skill names.</summary>
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    /// <summary>Gets or sets the list of tool names available to this agent.</summary>
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();
    /// <summary>Gets or sets whether this agent allows task delegation.</summary>
    public bool AllowsDelegation { get; init; } = true;
    /// <summary>Gets or sets the current workload count.</summary>
    public int CurrentWorkload { get; init; }
    /// <summary>Gets or sets the availability score, clamped to 0.0–1.0.</summary>
    public double AvailabilityScore
    {
        get => _availabilityScore;
        init => _availabilityScore = Math.Clamp(value, 0.0, 1.0);
    }
}

/// <summary>
/// Profile of an agent's capabilities.
/// </summary>
public sealed record AgentCapabilityProfile
{
    /// <summary>Gets or sets the agent identifier.</summary>
    public AgentId AgentId { get; init; } = AgentId.Create();
    /// <summary>Gets or sets skill proficiency scores keyed by skill name.</summary>
    public Dictionary<string, double> SkillScores { get; init; } = [];
    /// <summary>Gets or sets the agent's specializations.</summary>
    public IReadOnlyList<string> Specializations { get; init; } = Array.Empty<string>();
    /// <summary>Gets or sets task type execution counts.</summary>
    public Dictionary<string, int> TaskHistory { get; init; } = [];
    /// <summary>Gets or sets the overall performance score.</summary>
    public double OverallPerformanceScore { get; init; } = 1.0;
    /// <summary>Gets or sets when this profile was last updated.</summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}
