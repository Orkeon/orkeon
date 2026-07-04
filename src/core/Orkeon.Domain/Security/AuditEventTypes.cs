namespace Orkeon.Domain.Security;

/// <summary>
/// Represents an auditable event that occurred during crew execution.
/// </summary>
public record AuditEvent
{
    /// <summary>Gets the unique event identifier.</summary>
    public required string EventId { get; init; }
    /// <summary>Gets when the event occurred.</summary>
    public required DateTime Timestamp { get; init; }
    /// <summary>Gets the audit category.</summary>
    public required AuditCategory Category { get; init; }
    /// <summary>Gets the action that was performed.</summary>
    public required string Action { get; init; }
    /// <summary>Gets the outcome of the audited operation.</summary>
    public required AuditOutcome Outcome { get; init; }
    /// <summary>Gets the correlation identifier linking related events.</summary>
    public required string CorrelationId { get; init; }
    /// <summary>Gets the optional crew identifier.</summary>
    public string? CrewId { get; init; }
    /// <summary>Gets the optional agent role.</summary>
    public string? AgentRole { get; init; }
    /// <summary>Gets the optional task identifier.</summary>
    public string? TaskId { get; init; }
    /// <summary>Gets the optional duration in milliseconds.</summary>
    public long? DurationMs { get; init; }
    /// <summary>Gets additional event details.</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets the severity of the event.</summary>
    public AuditSeverity Severity { get; init; } = AuditSeverity.Info;
    /// <summary>Gets the optional human-readable message.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Categories of auditable events.
/// </summary>
public enum AuditCategory
{
    /// <summary>An LLM API call.</summary>
    LlmCall,
    /// <summary>Tool execution.</summary>
    ToolExecution,
    /// <summary>File system access.</summary>
    FileAccess,
    /// <summary>An HTTP request.</summary>
    HttpRequest,
    /// <summary>A security-related event.</summary>
    SecurityEvent,
    /// <summary>A crew lifecycle event.</summary>
    CrewLifecycle,
    /// <summary>An agent decision.</summary>
    AgentDecision,
    /// <summary>A memory read or write operation.</summary>
    MemoryOperation,
    /// <summary>A configuration change.</summary>
    ConfigChange
}

/// <summary>
/// Outcome of an audited operation.
/// </summary>
public enum AuditOutcome
{
    /// <summary>The operation succeeded.</summary>
    Success,
    /// <summary>The operation failed.</summary>
    Failure,
    /// <summary>The operation was blocked by a policy.</summary>
    Blocked,
    /// <summary>The operation was denied.</summary>
    Denied,
    /// <summary>The operation succeeded with a warning.</summary>
    Warning
}

/// <summary>
/// Severity level for audit events.
/// </summary>
public enum AuditSeverity
{
    /// <summary>Debug-level severity.</summary>
    Debug,
    /// <summary>Informational severity.</summary>
    Info,
    /// <summary>Warning severity.</summary>
    Warning,
    /// <summary>Error severity.</summary>
    Error,
    /// <summary>Critical severity.</summary>
    Critical
}
