using Microsoft.Extensions.Logging;
using Orkeon.Application.Agent.EventHandlers;
using Orkeon.Application.Crew.EventHandlers;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Tests.Services.EventHandlers;

public class AgentEventHandlerTests
{
    // ─── AgentCompletedTaskHandler ────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task AgentCompletedTaskHandler_ShouldHandleEvent()
    {
        // Arrange
        var logger = new TestLogger<AgentCompletedTaskHandler>();
        var handler = new AgentCompletedTaskHandler(logger);
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var domainEvent = new AgentCompletedTaskEvent
        {
            AgentId = agentId,
            TaskId = taskId,
            Output = TaskOutput.Text("Task completed successfully")
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert — handler completes without throwing
        Assert.True(true);
    }

    [Fact]
    public async System.Threading.Tasks.Task AgentCompletedTaskHandler_ShouldLogCompletion()
    {
        // Arrange
        var logger = new TestLogger<AgentCompletedTaskHandler>();
        var handler = new AgentCompletedTaskHandler(logger);
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var domainEvent = new AgentCompletedTaskEvent
        {
            AgentId = agentId,
            TaskId = taskId,
            Output = TaskOutput.Text("Done")
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        Assert.True(logger.LogEntries.Count > 0);
        var logEntry = logger.LogEntries[0];
        Assert.Equal(LogLevel.Information, logEntry.LogLevel);
        Assert.Contains("completed", logEntry.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentCompletedTaskHandler_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgentCompletedTaskHandler(null!));
    }

    // ─── AgentFailedTaskHandler ──────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task AgentFailedTaskHandler_ShouldHandleEvent()
    {
        // Arrange
        var logger = new TestLogger<AgentFailedTaskHandler>();
        var handler = new AgentFailedTaskHandler(logger);
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var domainEvent = new AgentFailedTaskEvent
        {
            AgentId = agentId,
            TaskId = taskId,
            Reason = "Connection timeout"
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert — handler completes without throwing
        Assert.True(true);
    }

    [Fact]
    public async System.Threading.Tasks.Task AgentFailedTaskHandler_ShouldLogFailure()
    {
        // Arrange
        var logger = new TestLogger<AgentFailedTaskHandler>();
        var handler = new AgentFailedTaskHandler(logger);
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var domainEvent = new AgentFailedTaskEvent
        {
            AgentId = agentId,
            TaskId = taskId,
            Reason = "Out of memory"
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        Assert.True(logger.LogEntries.Count > 0);
        var logEntry = logger.LogEntries[0];
        Assert.Equal(LogLevel.Warning, logEntry.LogLevel);
        Assert.Contains("failed", logEntry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Out of memory", logEntry.Message!);
    }

    [Fact]
    public void AgentFailedTaskHandler_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgentFailedTaskHandler(null!));
    }

    // ─── CrewExecutionCompletedHandler ───────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task CrewExecutionCompletedHandler_ShouldHandleEvent()
    {
        // Arrange
        var logger = new TestLogger<CrewExecutionCompletedHandler>();
        var handler = new CrewExecutionCompletedHandler(logger);
        var crewId = CrewId.Create();
        var processId = ProcessId.Create();
        var domainEvent = new CrewExecutionCompletedEvent
        {
            CrewId = crewId,
            ProcessId = processId,
            Duration = TimeSpan.FromSeconds(42),
            CompletedTasks = 5,
            FailedTasks = 1
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert — handler completes without throwing
        Assert.True(true);
    }

    [Fact]
    public async System.Threading.Tasks.Task CrewExecutionCompletedHandler_ShouldLogCompletion()
    {
        // Arrange
        var logger = new TestLogger<CrewExecutionCompletedHandler>();
        var handler = new CrewExecutionCompletedHandler(logger);
        var crewId = CrewId.Create();
        var processId = ProcessId.Create();
        var domainEvent = new CrewExecutionCompletedEvent
        {
            CrewId = crewId,
            ProcessId = processId,
            Duration = TimeSpan.FromMinutes(3),
            CompletedTasks = 10,
            FailedTasks = 0
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        Assert.True(logger.LogEntries.Count > 0);
        var logEntry = logger.LogEntries[0];
        Assert.Equal(LogLevel.Information, logEntry.LogLevel);
        Assert.Contains("completed", logEntry.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrewExecutionCompletedHandler_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CrewExecutionCompletedHandler(null!));
    }

    // ─── CrewExecutionFailedHandler ──────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task CrewExecutionFailedHandler_ShouldHandleEvent()
    {
        // Arrange
        var logger = new TestLogger<CrewExecutionFailedHandler>();
        var handler = new CrewExecutionFailedHandler(logger);
        var crewId = CrewId.Create();
        var processId = ProcessId.Create();
        var domainEvent = new CrewExecutionFailedEvent
        {
            CrewId = crewId,
            ProcessId = processId,
            Reason = "Agent unresponsive"
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert — handler completes without throwing
        Assert.True(true);
    }

    [Fact]
    public async System.Threading.Tasks.Task CrewExecutionFailedHandler_ShouldLogFailure()
    {
        // Arrange
        var logger = new TestLogger<CrewExecutionFailedHandler>();
        var handler = new CrewExecutionFailedHandler(logger);
        var crewId = CrewId.Create();
        var processId = ProcessId.Create();
        var domainEvent = new CrewExecutionFailedEvent
        {
            CrewId = crewId,
            ProcessId = processId,
            Reason = "LLM rate limit exceeded"
        };

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        Assert.True(logger.LogEntries.Count > 0);
        var logEntry = logger.LogEntries[0];
        Assert.Equal(LogLevel.Warning, logEntry.LogLevel);
        Assert.Contains("failed", logEntry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LLM rate limit exceeded", logEntry.Message!);
    }

    [Fact]
    public void CrewExecutionFailedHandler_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CrewExecutionFailedHandler(null!));
    }
}
