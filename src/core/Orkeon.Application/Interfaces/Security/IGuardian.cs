namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Phases during which a guardian check can be performed.
/// </summary>
public enum GuardPhase
{
    /// <summary>The composed user prompt of an agent turn, before the first provider call.</summary>
    Input,
    /// <summary>A tool call, before the tool runs.</summary>
    ToolExecution,
    /// <summary>A delegation (<c>delegate_work_to_coworker</c>, <c>ask_question_to_coworker</c>), before it runs.</summary>
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
    /// <summary>Block the operation. Guardians never rewrite content: they allow, warn or block.</summary>
    Block
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
    /// <summary>Gets or sets the id of the agent whose turn is checked.</summary>
    public string AgentId { get; init; } = string.Empty;
    /// <summary>Gets or sets the role of the agent whose turn is checked.</summary>
    public string AgentRole { get; init; } = string.Empty;
    /// <summary>Gets or sets the crew id.</summary>
    public string CrewId { get; init; } = string.Empty;
    /// <summary>Gets or sets the content checked by the <see cref="GuardPhase.Input"/> phase.</summary>
    public string? Content { get; init; }
    /// <summary>Gets or sets the tool name.</summary>
    public string? ToolName { get; init; }
    /// <summary>Gets or sets the tool call arguments.</summary>
    public IReadOnlyDictionary<string, object?>? ToolArgs { get; init; }
    /// <summary>
    /// Gets or sets the roles of the agents that delegated, outermost first, down to the one
    /// whose call is checked (excluded): empty outside a synchronous delegation.
    /// </summary>
    public IReadOnlyList<string> DelegationChain { get; init; } = [];
    /// <summary>Gets the delegation depth: how many delegations the checked call is nested in.</summary>
    public int DelegationDepth => DelegationChain.Count;
    /// <summary>Gets or sets the role a delegation targets.</summary>
    public string? TargetAgentRole { get; init; }
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

/// <summary>
/// Runs the guardians registered for a phase. The port the agent turn calls: the input phase
/// from the execution orchestrator, the tool and delegation phases from
/// <see cref="IToolInvocationPipeline"/>.
/// </summary>
public interface IGuardianPipeline
{
    /// <summary>
    /// Executes the guardians of <see cref="GuardContext.Phase"/>. Returns a block as soon as
    /// one guardian blocks; otherwise the aggregated warnings, or an allow.
    /// </summary>
    System.Threading.Tasks.Task<GuardResult> ExecuteAsync(GuardContext context, CancellationToken ct = default);
}
