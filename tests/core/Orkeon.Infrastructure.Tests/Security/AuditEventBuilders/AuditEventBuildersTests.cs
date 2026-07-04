using Orkeon.Domain.Security;
using AuditEventBuildersSut = Orkeon.Domain.Security.AuditEventBuilders;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.Security;

public class AuditEventBuildersTests
{
    [Fact]
    public void ShouldCreateCorrectEvent_WhenBuildingLlmCall()
    {
        // Act
        var evt = AuditEventBuildersSut.LlmCall(
            correlationId: "corr-1",
            provider: ProviderOpenAI,
            model: ModelGpt4,
            promptTokens: 100,
            completionTokens: 50,
            estimatedCost: 0.005m,
            durationMs: 1500,
            outcome: AuditOutcome.Success,
            agentRole: "researcher",
            crewId: "crew-42");

        // Assert
        Assert.NotNull(evt.EventId);
        Assert.Equal(32, evt.EventId.Length); // Guid without hyphens
        Assert.Equal(AuditCategory.LlmCall, evt.Category);
        Assert.Equal(AuditOutcome.Success, evt.Outcome);
        Assert.Equal("corr-1", evt.CorrelationId);
        Assert.Equal("researcher", evt.AgentRole);
        Assert.Equal("crew-42", evt.CrewId);
        Assert.Equal(1500, evt.DurationMs);
        Assert.Equal(AuditSeverity.Info, evt.Severity);
        Assert.Equal(ProviderOpenAI, evt.Details["provider"]);
        Assert.Equal(ModelGpt4, evt.Details["model"]);
        Assert.Equal("100", evt.Details["promptTokens"]);
        Assert.Equal("50", evt.Details["completionTokens"]);
        Assert.Contains("0.005", evt.Details["estimatedCost"]);
    }

    [Fact]
    public void ShouldCreateCorrectEvent_WhenBuildingToolExecution()
    {
        // Act
        var evt = AuditEventBuildersSut.ToolExecution(
            correlationId: "corr-2",
            toolName: "file_read",
            agentRole: "coder",
            outcome: AuditOutcome.Success,
            durationMs: 200,
            parameters: "{\"path\":\"/tmp/test.txt\"}");

        // Assert
        Assert.Equal(AuditCategory.ToolExecution, evt.Category);
        Assert.Equal("coder", evt.AgentRole);
        Assert.Equal(200, evt.DurationMs);
        Assert.Equal("file_read", evt.Details["toolName"]);
        Assert.Contains("/tmp/test.txt", evt.Details["parameters"]);
    }

    [Fact]
    public void ShouldCreateCorrectEventWithWarningSeverity_WhenFileAccessIsBlocked()
    {
        // Act
        var evt = AuditEventBuildersSut.FileAccess(
            correlationId: "corr-3",
            operation: "write",
            path: "/etc/passwd",
            outcome: AuditOutcome.Blocked,
            agentRole: "hacker",
            denialReason: "Path outside allowed directory");

        // Assert
        Assert.Equal(AuditCategory.FileAccess, evt.Category);
        Assert.Equal(AuditOutcome.Blocked, evt.Outcome);
        Assert.Equal(AuditSeverity.Warning, evt.Severity);
        Assert.Equal("Path outside allowed directory", evt.Details["denialReason"]);
        Assert.Equal("write", evt.Details["operation"]);
    }

    [Fact]
    public void ShouldCreateCorrectEventWithCriticalSeverity_WhenBuildingSecurityEvent()
    {
        // Act
        var evt = AuditEventBuildersSut.SecurityEvent(
            correlationId: "corr-4",
            threatType: "PromptInjection",
            description: "Detected prompt injection attempt",
            severity: AuditSeverity.Critical,
            agentRole: "analyst");

        // Assert
        Assert.Equal(AuditCategory.SecurityEvent, evt.Category);
        Assert.Equal(AuditSeverity.Critical, evt.Severity);
        Assert.Equal("PromptInjection", evt.Details["threatType"]);
        Assert.Equal("Detected prompt injection attempt", evt.Details["description"]);
        Assert.Equal("analyst", evt.AgentRole);
    }

    [Fact]
    public void ShouldTruncateAndAppendEllipsis_WhenStringExceedsMaxLength()
    {
        // Arrange
        var longString = new string('x', 1000);

        // Act
        var result = AuditEventBuildersSut.TruncateForAudit(longString);

        // Assert
        Assert.Equal(503, result.Length); // 500 + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void ShouldReturnUnchangedString_WhenStringIsShort()
    {
        // Act
        var result = AuditEventBuildersSut.TruncateForAudit("hello");

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenStringIsNullOrEmpty()
    {
        Assert.Equal(string.Empty, AuditEventBuildersSut.TruncateForAudit(null!));
        Assert.Equal(string.Empty, AuditEventBuildersSut.TruncateForAudit(""));
    }

    [Fact]
    public void ShouldCreateCorrectEvent_WhenBuildingHttpRequest()
    {
        // Act
        var evt = AuditEventBuildersSut.HttpRequest(
            correlationId: "corr-5",
            url: new Uri("https://api.example.com/data"),
            method: "GET",
            statusCode: 200,
            outcome: AuditOutcome.Success,
            durationMs: 350,
            agentRole: "fetcher");

        // Assert
        Assert.Equal(AuditCategory.HttpRequest, evt.Category);
        Assert.Equal(AuditOutcome.Success, evt.Outcome);
        Assert.Equal(350, evt.DurationMs);
        Assert.Equal("https://api.example.com/data", evt.Details[ParamUrl]);
        Assert.Equal("GET", evt.Details["method"]);
        Assert.Equal("200", evt.Details["statusCode"]);
        Assert.Equal("fetcher", evt.AgentRole);
    }

    [Fact]
    public void ShouldCreateCorrectEvent_WhenBuildingCrewLifecycle()
    {
        // Act
        var evt = AuditEventBuildersSut.CrewLifecycle(
            correlationId: "corr-6",
            crewId: "crew-100",
            action: "started",
            outcome: AuditOutcome.Success,
            message: "Crew execution started with 3 agents");

        // Assert
        Assert.Equal(AuditCategory.CrewLifecycle, evt.Category);
        Assert.Equal("crew-100", evt.CrewId);
        Assert.Equal("crew-100", evt.Details["crewId"]);
        Assert.Equal("started", evt.Details["action"]);
        Assert.Equal("Crew execution started with 3 agents", evt.Message);
    }
}
