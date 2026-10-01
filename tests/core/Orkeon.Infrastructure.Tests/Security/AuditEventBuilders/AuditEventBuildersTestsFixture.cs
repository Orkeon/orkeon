using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class AuditEventBuildersTestsFixture
{
    // --- Execution: ToolExecution ---
    public static AuditEvent BuildToolExecution(
        string correlationId = "corr-2",
        string toolName = "file_read",
        string agentRole = "coder",
        AuditOutcome outcome = AuditOutcome.Success,
        int durationMs = 200,
        string parameters = "{\"path\":\"/tmp/test.txt\"}")
        => AuditEventBuilders.ToolExecution(
            correlationId, toolName, agentRole, outcome, durationMs, parameters);

    // --- Execution: SecurityEvent ---
    public static AuditEvent BuildSecurityEvent(
        string correlationId = "corr-4",
        string threatType = "PromptInjection",
        string description = "Detected prompt injection attempt",
        AuditSeverity severity = AuditSeverity.Critical,
        string agentRole = "analyst")
        => AuditEventBuilders.SecurityEvent(
            correlationId, threatType, description, severity, agentRole);

    // --- Execution: TruncateForAudit ---
    public static string TruncateForAudit(string? value)
        => AuditEventBuilders.TruncateForAudit(value!);
}
