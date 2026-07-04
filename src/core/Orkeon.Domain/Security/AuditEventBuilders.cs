using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Security;

/// <summary>
/// Static builder methods for creating strongly-typed audit events.
/// </summary>
public static class AuditEventBuilders
{
    private const int MaxAuditStringLength = 500;

    /// <summary>
    /// Creates an audit event for an LLM call.
    /// </summary>
    public static AuditEvent LlmCall(LlmCallAuditInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Category = AuditCategory.LlmCall,
            Action = $"LLM call to {info.Provider}/{info.Model}",
            Outcome = info.Outcome,
            CorrelationId = info.CorrelationId,
            AgentRole = info.AgentRole,
            CrewId = info.CrewId,
            DurationMs = info.DurationMs,
            Severity = info.Outcome == AuditOutcome.Success ? AuditSeverity.Info : AuditSeverity.Warning,
            Details = new Dictionary<string, string>
            {
                ["provider"] = info.Provider,
                ["model"] = info.Model,
                ["promptTokens"] = info.PromptTokens.ToString(CultureInfo.InvariantCulture),
                ["completionTokens"] = info.CompletionTokens.ToString(CultureInfo.InvariantCulture),
                ["estimatedCost"] = Inv.ToString(info.EstimatedCost, "F6")
            }
        };
    }

    /// <summary>
    /// Creates an audit event for an LLM call.
    /// Convenience overload that delegates to <see cref="LlmCall(LlmCallAuditInfo)"/>.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use LlmCall(LlmCallAuditInfo) instead
    public static AuditEvent LlmCall(
        string correlationId,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        decimal estimatedCost,
        long durationMs,
        AuditOutcome outcome,
        string? agentRole = null,
        string? crewId = null)
    {
        return LlmCall(new LlmCallAuditInfo
        {
            CorrelationId = correlationId,
            Provider = provider,
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            EstimatedCost = estimatedCost,
            DurationMs = durationMs,
            Outcome = outcome,
            AgentRole = agentRole,
            CrewId = crewId
        });
    }
#pragma warning restore S107

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
    /// Creates an audit event for a file access operation.
    /// </summary>
    public static AuditEvent FileAccess(
        string correlationId,
        string operation,
        string path,
        AuditOutcome outcome,
        string agentRole,
        string? denialReason = null)
    {
        var details = new Dictionary<string, string>
        {
            ["operation"] = operation,
            ["path"] = TruncateForAudit(path)
        };

        if (denialReason is not null)
        {
            details["denialReason"] = denialReason;
        }

        var severity = outcome switch
        {
            AuditOutcome.Blocked or AuditOutcome.Denied => AuditSeverity.Warning,
            AuditOutcome.Failure => AuditSeverity.Error,
            _ => AuditSeverity.Info
        };

        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Category = AuditCategory.FileAccess,
            Action = $"File {operation}: {TruncateForAudit(path)}",
            Outcome = outcome,
            CorrelationId = correlationId,
            AgentRole = agentRole,
            Severity = severity,
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
    /// Creates an audit event for an HTTP request.
    /// </summary>
    public static AuditEvent HttpRequest(
        string correlationId,
        Uri url,
        string method,
        int statusCode,
        AuditOutcome outcome,
        long durationMs,
        string? agentRole = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        var urlText = url.ToString();
        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Category = AuditCategory.HttpRequest,
            Action = $"HTTP {method} {TruncateForAudit(urlText)}",
            Outcome = outcome,
            CorrelationId = correlationId,
            AgentRole = agentRole,
            DurationMs = durationMs,
            Severity = outcome == AuditOutcome.Success ? AuditSeverity.Info : AuditSeverity.Warning,
            Details = new Dictionary<string, string>
            {
                ["url"] = TruncateForAudit(urlText),
                ["method"] = method,
                ["statusCode"] = statusCode.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    /// <summary>
    /// Creates an audit event for a crew lifecycle event.
    /// </summary>
    public static AuditEvent CrewLifecycle(
        string correlationId,
        string crewId,
        string action,
        AuditOutcome outcome,
        string? message = null)
    {
        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Category = AuditCategory.CrewLifecycle,
            Action = $"Crew {action}",
            Outcome = outcome,
            CorrelationId = correlationId,
            CrewId = crewId,
            Severity = outcome == AuditOutcome.Success ? AuditSeverity.Info : AuditSeverity.Warning,
            Message = message is not null ? TruncateForAudit(message) : null,
            Details = new Dictionary<string, string>
            {
                ["crewId"] = crewId,
                ["action"] = action
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
