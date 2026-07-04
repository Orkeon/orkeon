using Orkeon.Domain.Security;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Security;

public class AuditEventBuildersTestsFixture
{
    // --- Execution: LlmCall ---
    public static AuditEvent BuildLlmCall(
        string correlationId = "corr-1",
        string provider = ProviderOpenAI,
        string model = ModelGpt4,
        int promptTokens = 100,
        int completionTokens = 50,
        decimal estimatedCost = 0.005m,
        int durationMs = 1500,
        AuditOutcome outcome = AuditOutcome.Success,
        string agentRole = "researcher",
        string crewId = "crew-42")
        => AuditEventBuilders.LlmCall(
            correlationId, provider, model,
            promptTokens, completionTokens, estimatedCost,
            durationMs, outcome, agentRole, crewId);

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

    // --- Execution: FileAccess ---
    public static AuditEvent BuildFileAccess(
        string correlationId = "corr-3",
        string operation = "write",
        string path = "/etc/passwd",
        AuditOutcome outcome = AuditOutcome.Blocked,
        string agentRole = "hacker",
        string? denialReason = "Path outside allowed directory")
        => AuditEventBuilders.FileAccess(
            correlationId, operation, path, outcome, agentRole, denialReason);

    // --- Execution: SecurityEvent ---
    public static AuditEvent BuildSecurityEvent(
        string correlationId = "corr-4",
        string threatType = "PromptInjection",
        string description = "Detected prompt injection attempt",
        AuditSeverity severity = AuditSeverity.Critical,
        string agentRole = "analyst")
        => AuditEventBuilders.SecurityEvent(
            correlationId, threatType, description, severity, agentRole);

    // --- Execution: HttpRequest ---
    public static AuditEvent BuildHttpRequest(
        string correlationId = "corr-5",
        string url = "https://api.example.com/data",
        string method = "GET",
        int statusCode = 200,
        AuditOutcome outcome = AuditOutcome.Success,
        int durationMs = 350,
        string agentRole = "fetcher")
        => AuditEventBuilders.HttpRequest(
            correlationId, new Uri(url), method, statusCode, outcome, durationMs, agentRole);

    // --- Execution: CrewLifecycle ---
    public static AuditEvent BuildCrewLifecycle(
        string correlationId = "corr-6",
        string crewId = "crew-100",
        string action = "started",
        AuditOutcome outcome = AuditOutcome.Success,
        string message = "Crew execution started with 3 agents")
        => AuditEventBuilders.CrewLifecycle(
            correlationId, crewId, action, outcome, message);

    // --- Execution: TruncateForAudit ---
    public static string TruncateForAudit(string? value)
        => AuditEventBuilders.TruncateForAudit(value!);
}
