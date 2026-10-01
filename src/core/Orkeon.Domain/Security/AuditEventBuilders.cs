namespace Orkeon.Domain.Security;

/// <summary>
/// Static builder methods for the audit events the framework writes: a tool execution per
/// call of the tool-invocation pipeline, a security event per guardian block or warning and
/// per injection pattern found in a tool result (GAP-09).
/// </summary>
public static class AuditEventBuilders
{
    private const int MaxAuditStringLength = 500;

    /// <summary>
    /// Creates an audit event for a tool execution.
    /// </summary>
    public static AuditEvent ToolExecution(
        string correlationId,
        string toolName,
        string agentRole,
        AuditOutcome outcome,
        long durationMs,
        string? parameters = null)
    {
        var details = new Dictionary<string, string>
        {
            ["toolName"] = toolName
        };

        if (parameters is not null)
        {
            details["parameters"] = TruncateForAudit(parameters);
        }

        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Category = AuditCategory.ToolExecution,
            Action = $"Tool execution: {toolName}",
            Outcome = outcome,
            CorrelationId = correlationId,
            AgentRole = agentRole,
            DurationMs = durationMs,
            Severity = outcome == AuditOutcome.Success ? AuditSeverity.Info : AuditSeverity.Warning,
            Details = details
        };
    }

    /// <summary>
    /// Creates an audit event for a security-related event.
    /// </summary>
    public static AuditEvent SecurityEvent(
        string correlationId,
        string threatType,
        string description,
        AuditSeverity severity,
        string? agentRole = null)
    {
        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Category = AuditCategory.SecurityEvent,
            Action = $"Security event: {threatType}",
            Outcome = AuditOutcome.Warning,
            CorrelationId = correlationId,
            AgentRole = agentRole,
            Severity = severity,
            Message = TruncateForAudit(description),
            Details = new Dictionary<string, string>
            {
                ["threatType"] = threatType,
                ["description"] = TruncateForAudit(description)
            }
        };
    }

    /// <summary>
    /// Truncates a string to the maximum audit length.
    /// </summary>
    public static string TruncateForAudit(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        return value.Length <= MaxAuditStringLength
            ? value
            : string.Concat(value.AsSpan(0, MaxAuditStringLength), "...");
    }
}
