using Orkeon.Domain.Common;
using Orkeon.Domain.Memory.ValueObjects;
using Orkeon.Domain.Tools;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for ToolUsage following Clean Architecture principles.
/// Tests the business rules and validation logic of the ToolUsage class.
/// </summary>
public class ToolUsageTests
{
    [Fact]
    public void ShouldCreateToolUsage_WhenConstructingWithValidParameters()
    {
        // Arrange
        var toolId = "tool-123";
        var toolName = "FileReadTool";
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var duration = TimeSpan.FromSeconds(2.5);
        var success = true;
        var error = "No error";
        var metadata = ToolUsageMetadata.Empty;

        // Act
        var toolUsage = new ToolUsage(
            new ToolCallIdentity(toolId, toolName, agentId, taskId),
            duration,
            success,
            error,
            metadata);

        // Assert
        Assert.Equal(toolId, toolUsage.ToolId);
        Assert.Equal(toolName, toolUsage.ToolName);
        Assert.Equal(agentId, toolUsage.AgentId);
        Assert.Equal(taskId, toolUsage.TaskId);
        Assert.Equal(duration, toolUsage.Duration);
        Assert.Equal(success, toolUsage.Success);
        Assert.Equal(error, toolUsage.Error);
        Assert.Same(metadata, toolUsage.Metadata);
        Assert.True(toolUsage.UsedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullToolId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ToolUsage(
                new ToolCallIdentity(null!, "ToolName", "AgentId", "TaskId"),
                TimeSpan.FromSeconds(1),
                true));

        Assert.Equal(nameof(ToolCallIdentity.ToolId), exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullToolName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ToolUsage(
                new ToolCallIdentity("ToolId", null!, "AgentId", "TaskId"),
                TimeSpan.FromSeconds(1),
                true));

        Assert.Equal(nameof(ToolCallIdentity.ToolName), exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullAgentId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ToolUsage(
                new ToolCallIdentity("ToolId", "ToolName", null!, "TaskId"),
                TimeSpan.FromSeconds(1),
                true));

        Assert.Equal(nameof(ToolCallIdentity.AgentId), exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullTaskId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ToolUsage(
                new ToolCallIdentity("ToolId", "ToolName", "AgentId", null!),
                TimeSpan.FromSeconds(1),
                true));

        Assert.Equal(nameof(ToolCallIdentity.TaskId), exception.ParamName);
    }

    [Fact]
    public void ShouldAllowNull_WhenConstructingWithNullError()
    {
        // Act
        var toolUsage = new ToolUsage(
            new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"),
            TimeSpan.FromSeconds(1),
            true,
            null);

        // Assert
        Assert.Null(toolUsage.Error);
    }

    [Fact]
    public void ShouldUseEmpty_WhenConstructingWithNullMetadata()
    {
        // Act
        var toolUsage = new ToolUsage(
            new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"),
            TimeSpan.FromSeconds(1),
            true,
            null);

        // Assert
        Assert.Same(ToolUsageMetadata.Empty, toolUsage.Metadata);
    }

    [Fact]
    public void ShouldBeUtc_WhenUsingUsedAt()
    {
        // Act
        var toolUsage = new ToolUsage(
            new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"),
            TimeSpan.FromSeconds(1),
            true);

        // Assert
        Assert.Equal(DateTimeKind.Utc, toolUsage.UsedAt.Kind);
    }

    [Fact]
    public void ShouldCreateSuccessfulToolUsage_WhenCreatingSuccess()
    {
        // Arrange
        var toolId = "tool-success";
        var toolName = "SuccessTool";
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var duration = TimeSpan.FromSeconds(1.5);
        var metadata = ToolUsageMetadata.Empty;

        // Act
        var toolUsage = ToolUsage.CreateSuccess(new ToolCallIdentity(toolId, toolName, agentId, taskId), duration, metadata);

        // Assert
        Assert.Equal(toolId, toolUsage.ToolId);
        Assert.Equal(toolName, toolUsage.ToolName);
        Assert.Equal(agentId, toolUsage.AgentId);
        Assert.Equal(taskId, toolUsage.TaskId);
        Assert.Equal(duration, toolUsage.Duration);
        Assert.True(toolUsage.Success);
        Assert.Null(toolUsage.Error);
        Assert.Same(metadata, toolUsage.Metadata);
    }

    [Fact]
    public void ShouldUseEmpty_WhenCreatingSuccessWithNullMetadata()
    {
        // Act
        var toolUsage = ToolUsage.CreateSuccess(new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"), TimeSpan.FromSeconds(1), null);

        // Assert
        Assert.Same(ToolUsageMetadata.Empty, toolUsage.Metadata);
    }

    [Fact]
    public void ShouldCreateFailedToolUsage_WhenCreatingFailure()
    {
        // Arrange
        var toolId = "tool-fail";
        var toolName = "FailTool";
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var duration = TimeSpan.FromSeconds(0.5);
        var error = "Tool execution failed";
        var metadata = ToolUsageMetadata.Empty;

        // Act
        var toolUsage = ToolUsage.CreateFailure(new ToolCallIdentity(toolId, toolName, agentId, taskId), duration, error, metadata);

        // Assert
        Assert.Equal(toolId, toolUsage.ToolId);
        Assert.Equal(toolName, toolUsage.ToolName);
        Assert.Equal(agentId, toolUsage.AgentId);
        Assert.Equal(taskId, toolUsage.TaskId);
        Assert.Equal(duration, toolUsage.Duration);
        Assert.False(toolUsage.Success);
        Assert.Equal(error, toolUsage.Error);
        Assert.Same(metadata, toolUsage.Metadata);
    }

    [Fact]
    public void ShouldUseEmpty_WhenCreatingFailureWithNullMetadata()
    {
        // Act
        var toolUsage = ToolUsage.CreateFailure(new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"), TimeSpan.FromSeconds(1), "Error message", null);

        // Assert
        Assert.Same(ToolUsageMetadata.Empty, toolUsage.Metadata);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5000)]
    public void ShouldAcceptAll_WhenUsingDurationWithVariousValues(int milliseconds)
    {
        // Arrange
        var duration = TimeSpan.FromMilliseconds(milliseconds);

        // Act
        var toolUsage = new ToolUsage(
            new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"),
            duration,
            true);

        // Assert
        Assert.Equal(duration, toolUsage.Duration);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldAcceptAll_WhenUsingSuccessWithBothValues(bool success)
    {
        // Act
        var toolUsage = new ToolUsage(
            new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"),
            TimeSpan.FromSeconds(1),
            success);

        // Assert
        Assert.Equal(success, toolUsage.Success);
    }

    [Fact]
    public void ShouldSetUsedAtCorrectly_WhenUsingFactoryMethods()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var successUsage = ToolUsage.CreateSuccess(new ToolCallIdentity("T1", "Tool", "A1", "Task1"), TimeSpan.FromSeconds(1));
        var failureUsage = ToolUsage.CreateFailure(new ToolCallIdentity("T2", "Tool", "A2", "Task2"), TimeSpan.FromSeconds(1), "Error");

        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.InRange(successUsage.UsedAt, beforeCreation, afterCreation);
        Assert.InRange(failureUsage.UsedAt, beforeCreation, afterCreation);
    }

    [Fact]
    public void ShouldStoreMetadata_WhenUsingFactoryMethodsWithComplexMetadata()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .Add("inputSize", 1024)
            .Add("outputFormat", "json")
            .Build();

        // Act
        var successUsage = ToolUsage.CreateSuccess(new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"), TimeSpan.FromSeconds(2), metadata);

        var failureUsage = ToolUsage.CreateFailure(new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"), TimeSpan.FromSeconds(1), Failed, metadata);

        // Assert
        Assert.Same(metadata, successUsage.Metadata);
        Assert.Same(metadata, failureUsage.Metadata);
    }

    [Fact]
    public void ShouldAcceptEmptyString_WhenCreatingFailureWithEmptyError()
    {
        // Act
        var toolUsage = ToolUsage.CreateFailure(new ToolCallIdentity("ToolId", "ToolName", "AgentId", "TaskId"), TimeSpan.FromSeconds(1), string.Empty);

        // Assert
        Assert.False(toolUsage.Success);
        Assert.Equal(string.Empty, toolUsage.Error);
    }

    [Fact]
    public void ShouldHaveUniqueTimestamps_WhenUsingMultipleToolUsages()
    {
        // Arrange
        var usages = new List<ToolUsage>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            usages.Add(ToolUsage.CreateSuccess(new ToolCallIdentity($"tool-{i}", "TestTool", "TestAgent", "TestTask"), TimeSpan.FromMilliseconds(i * 10)));

            // Ensure different timestamps (deterministic clock advance, R5.6)
            ClockAdvance.UntilStrictlyAfter(usages[^1].UsedAt);
        }

        // Assert
        var timestamps = usages.Select(u => u.UsedAt).ToList();
        Assert.Equal(timestamps.Count, timestamps.Distinct().Count());
    }

    [Fact]
    public void ShouldAcceptLongDuration_WhenConstructingWithLongDuration()
    {
        // Arrange
        var longDuration = TimeSpan.FromHours(2);

        // Act
        var toolUsage = new ToolUsage(
            new ToolCallIdentity("ToolId", "LongRunningTool", "AgentId", "TaskId"),
            longDuration,
            true);

        // Assert
        Assert.Equal(longDuration, toolUsage.Duration);
    }
}
