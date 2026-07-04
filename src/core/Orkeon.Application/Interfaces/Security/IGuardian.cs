namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Phases during which a guardian check can be performed.
/// </summary>
public enum GuardPhase
{
    /// <summary>Input.</summary>
    Input,
    /// <summary>Output.</summary>
    Output,
    /// <summary>Tool Execution.</summary>
    ToolExecution,
    /// <summary>Delegation.</summary>
    Delegation
}

/// <summary>
/// Action taken by a guardian in response to a check.
/// </summary>
public enum GuardAction
{
    /// <summary>Allow the operation to proceed.</summary>
    Allow,
    /// <summary>Allow with a warning.</summary>
    Warn,
    /// <summary>Block the operation.</summary>
    Block,
    /// <summary>Modify the content before proceeding.</summary>
    Modify
}

/// <summary>
/// Severity level for guardian violations.
/// </summary>
public enum GuardThreatSeverity
{
    /// <summary>No threat detected.</summary>
    None,
    /// <summary>Low severity threat.</summary>
    Low,
    /// <summary>Medium severity threat.</summary>
    Medium,
    /// <summary>High severity threat.</summary>
    High,
    /// <summary>Critical severity threat.</summary>
    Critical
}

/// <summary>
/// Represents a violation detected by a guardian.
/// </summary>
public record GuardViolation(
    string GuardName,
    GuardPhase Phase,
    string Description,
    GuardThreatSeverity Severity,
    DateTime Timestamp);

/// <summary>
/// Context passed to a guardian for evaluation.
/// </summary>
public record GuardContext
{
    /// <summary>Gets or sets the phase.</summary>
    public GuardPhase Phase { get; init; }
    /// <summary>Gets or sets the agent id.</summary>
    public string AgentId { get; init; } = string.Empty;
    /// <summary>Gets or sets the crew id.</summary>
    public string CrewId { get; init; } = string.Empty;
    /// <summary>Gets or sets the content.</summary>
    public string? Content { get; init; }
    /// <summary>Gets or sets the tool name.</summary>
    public string? ToolName { get; init; }
    /// <summary>Tool Args.</summary>
    public Dictionary<string, object>? ToolArgs { get; init; }
    /// <summary>Gets or sets the delegation depth.</summary>
    public int DelegationDepth { get; init; }
    /// <summary>Gets or sets the target agent id.</summary>
    public string? TargetAgentId { get; init; }
}

/// <summary>
/// Result of a guardian check.
/// </summary>
public record GuardResult
{
    /// <summary>
    /// Gets or sets a value indicating whether is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }
    /// <summary>Gets or sets the action.</summary>
    public GuardAction Action { get; init; }
    /// <summary>Gets or sets the reason.</summary>
    public string? Reason { get; init; }
    /// <summary>Gets or sets the violations.</summary>
    public IReadOnlyList<GuardViolation> Violations { get; init; } = Array.Empty<GuardViolation>();

    /// <summary>
    /// Allow.
    /// </summary>
    public static GuardResult Allow() => new() { IsAllowed = true, Action = GuardAction.Allow };

    /// <summary>
    /// Warn.
    /// </summary>
    public static GuardResult Warn(string reason, IReadOnlyList<GuardViolation> violations)
        => new() { IsAllowed = true, Action = GuardAction.Warn, Reason = reason, Violations = violations };

    /// <summary>
    /// Block.
    /// </summary>
    public static GuardResult Block(string reason, IReadOnlyList<GuardViolation> violations)
        => new() { IsAllowed = false, Action = GuardAction.Block, Reason = reason, Violations = violations };
}

/// <summary>
/// A guardian that checks a context and returns a result indicating whether the operation is allowed.
/// </summary>
public interface IGuardian
{
    /// <summary>
    /// Checks the given context and returns whether the operation should be allowed, warned, or blocked.
    /// </summary>
    System.Threading.Tasks.Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default);
}
