using Orkeon.Domain.Security;
using AuditEventBuildersSut = Orkeon.Domain.Security.AuditEventBuilders;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.Security;

public class AuditEventBuildersTests
{
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
}
