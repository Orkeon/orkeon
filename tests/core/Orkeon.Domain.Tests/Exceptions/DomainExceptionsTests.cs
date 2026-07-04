using Orkeon.Domain.Exceptions;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
namespace Orkeon.Domain.Tests.Exceptions;

/// <summary>
/// Tests for Domain Exceptions following Clean Architecture principles.
/// Tests all domain exception classes and their behavior.
/// </summary>
public class DomainExceptionsTests
{
    #region DomainException Tests (Abstract Base Class)

    // Create concrete implementation for testing abstract class
    private class TestDomainException : DomainException
    {
        public TestDomainException(string message) : base(message) { }
        public TestDomainException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Fact]
    public void ShouldSetMessage_WhenUsingDomainExceptionWithMessage()
    {
        // Arrange
        var message = "Test domain exception message";

        // Act
        var exception = new TestDomainException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ShouldSetBoth_WhenUsingDomainExceptionWithMessageAndInnerException()
    {
        // Arrange
        var message = "Test domain exception message";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new TestDomainException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    #endregion

    #region AgentException Tests

    [Fact]
    public void ShouldSetProperties_WhenUsingAgentExceptionWithAgentIdAndMessage()
    {
        // Arrange
        var agentId = AgentId.Create();
        var message = "Agent failed to execute task";

        // Act
        var exception = new AgentException(agentId, message);

        // Assert
        Assert.Equal(agentId, exception.AgentId);
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ShouldSetAllProperties_WhenUsingAgentExceptionWithAgentIdMessageAndInnerException()
    {
        // Arrange
        var agentId = AgentId.Create();
        var message = "Agent encountered an error";
        var innerException = new System.TimeoutException("Operation timed out");

        // Act
        var exception = new AgentException(agentId, message, innerException);

        // Assert
        Assert.Equal(agentId, exception.AgentId);
        Assert.Equal(message, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void ShouldAcceptAll_WhenUsingAgentExceptionWithVariousAgentIds()
    {
        // Act
        var agentId = AgentId.Create();
        var exception = new AgentException(agentId, "Test error");

        // Assert
        Assert.Equal(agentId, exception.AgentId);
    }

    #endregion

    #region TaskException Tests

    [Fact]
    public void ShouldSetProperties_WhenUsingTaskExceptionWithTaskIdAndMessage()
    {
        // Arrange
        var taskId = TaskId.Create();
        var message = "Task execution failed";

        // Act
        var exception = new TaskException(taskId, message);

        // Assert
        Assert.Equal(taskId, exception.TaskId);
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ShouldSetAllProperties_WhenUsingTaskExceptionWithTaskIdMessageAndInnerException()
    {
        // Arrange
        var taskId = TaskId.Create();
        var message = "Task validation failed";
        var innerException = new ArgumentNullException("paramName");

        // Act
        var exception = new TaskException(taskId, message, innerException);

        // Assert
        Assert.Equal(taskId, exception.TaskId);
        Assert.Equal(message, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    #endregion

    #region CrewException Tests

    [Fact]
    public void ShouldSetProperties_WhenUsingCrewExceptionWithCrewIdAndMessage()
    {
        // Arrange
        var crewId = CrewId.Create();
        var message = "Crew execution failed";

        // Act
        var exception = new CrewException(crewId, message);

        // Assert
        Assert.Equal(crewId, exception.CrewId);
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ShouldSetAllProperties_WhenUsingCrewExceptionWithCrewIdMessageAndInnerException()
    {
        // Arrange
        var crewId = CrewId.Create();
        var message = "Crew initialization failed";
        var innerException = new InvalidOperationException("Invalid crew configuration");

        // Act
        var exception = new CrewException(crewId, message, innerException);

        // Assert
        Assert.Equal(crewId, exception.CrewId);
        Assert.Equal(message, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    #endregion

    #region ToolExecutionException Tests

    [Fact]
    public void ShouldFormatMessage_WhenUsingToolExecutionExceptionWithToolNameAndMessage()
    {
        // Arrange
        var toolName = "WebSearchTool";
        var message = "Unable to connect to search API";

        // Act
        var exception = new ToolExecutionException(toolName, message);

        // Assert
        Assert.Equal(toolName, exception.ToolName);
        Assert.Equal($"Tool '{toolName}' failed: {message}", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ShouldFormatMessageWithInner_WhenUsingToolExecutionExceptionWithToolNameMessageAndInnerException()
    {
        // Arrange
        var toolName = "DatabaseQueryTool";
        var message = "Query execution failed";
        var innerException = new InvalidOperationException("Connection lost");

        // Act
        var exception = new ToolExecutionException(toolName, message, innerException);

        // Assert
        Assert.Equal(toolName, exception.ToolName);
        Assert.Equal($"Tool '{toolName}' failed: {message}", exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Theory]
    [InlineData("FileReadTool", "File not found")]
    [InlineData("HttpRequestTool", "Network timeout")]
    [InlineData("JsonParserTool", "Invalid JSON format")]
    public void ShouldFormatMessagesCorrectly_WhenUsingToolExecutionExceptionWithVariousTools(string toolName, string message)
    {
        // Act
        var exception = new ToolExecutionException(toolName, message);

        // Assert
        Assert.Contains(toolName, exception.Message);
        Assert.Contains(message, exception.Message);
        Assert.Equal($"Tool '{toolName}' failed: {message}", exception.Message);
    }

    #endregion

    #region MemoryException Tests

    [Fact]
    public void ShouldSetMessage_WhenUsingMemoryExceptionWithMessage()
    {
        // Arrange
        var message = "Memory store is full";

        // Act
        var exception = new MemoryException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ShouldSetBoth_WhenUsingMemoryExceptionWithMessageAndInnerException()
    {
        // Arrange
        var message = "Failed to access memory store";
        var innerException = new IOException("Disk full");

        // Act
        var exception = new MemoryException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    #endregion

    #region ValidationException Tests

    [Fact]
    public void ShouldFormatMessage_WhenUsingValidationExceptionWithPropertyNameAndMessage()
    {
        // Arrange
        var propertyName = "Email";
        var message = "Invalid email format";

        // Act
        var exception = new ValidationException(propertyName, message);

        // Assert
        Assert.Equal(propertyName, exception.PropertyName);
        Assert.Equal($"Validation failed for '{propertyName}': {message}", exception.Message);
    }

    [Theory]
    [InlineData("Username", "Username cannot be empty")]
    [InlineData("Age", "Age must be between 0 and 150")]
    [InlineData("Password", "Password must contain at least 8 characters")]
    public void ShouldFormatMessagesCorrectly_WhenUsingValidationExceptionWithVariousProperties(string propertyName, string message)
    {
        // Act
        var exception = new ValidationException(propertyName, message);

        // Assert
        Assert.Equal(propertyName, exception.PropertyName);
        Assert.Contains(propertyName, exception.Message);
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingValidationExceptionWithSpecialCharactersInPropertyName()
    {
        // Arrange
        var propertyName = "User.Profile.Email[0]";
        var message = "Invalid format";

        // Act
        var exception = new ValidationException(propertyName, message);

        // Assert
        Assert.Equal(propertyName, exception.PropertyName);
        Assert.Contains(propertyName, exception.Message);
    }

    #endregion

    #region NotFoundException Tests

    [Fact]
    public void ShouldFormatMessage_WhenUsingNotFoundExceptionWithResourceTypeAndId()
    {
        // Arrange
        var resourceType = "Agent";
        var resourceId = "agent-123";

        // Act
        var exception = new NotFoundException(resourceType, resourceId);

        // Assert
        Assert.Equal(resourceType, exception.ResourceType);
        Assert.Equal(resourceId, exception.ResourceId);
        Assert.Equal($"{resourceType} with ID '{resourceId}' was not found.", exception.Message);
    }

    [Theory]
    [InlineData("User", "user-456")]
    [InlineData("Task", "task-789")]
    [InlineData("Crew", "crew-abc")]
    [InlineData("Memory", "mem-xyz")]
    public void ShouldFormatMessagesCorrectly_WhenUsingNotFoundExceptionWithVariousResources(string resourceType, string resourceId)
    {
        // Act
        var exception = new NotFoundException(resourceType, resourceId);

        // Assert
        Assert.Equal(resourceType, exception.ResourceType);
        Assert.Equal(resourceId, exception.ResourceId);
        Assert.Contains(resourceType, exception.Message);
        Assert.Contains(resourceId, exception.Message);
    }

    [Fact]
    public void ShouldStillFormat_WhenUsingNotFoundExceptionWithEmptyResourceId()
    {
        // Arrange
        var resourceType = "Configuration";
        var resourceId = "";

        // Act
        var exception = new NotFoundException(resourceType, resourceId);

        // Assert
        Assert.Equal(resourceType, exception.ResourceType);
        Assert.Equal(resourceId, exception.ResourceId);
        Assert.Equal($"{resourceType} with ID '' was not found.", exception.Message);
    }

    #endregion

    #region TimeoutException Tests

    [Fact]
    public void ShouldFormatMessage_WhenUsingTimeoutExceptionWithOperationAndTimeout()
    {
        // Arrange
        var operation = "Database query";
        var timeout = TimeoutQuick;

        // Act
        var exception = new Orkeon.Domain.Exceptions.TimeoutException(operation, timeout);

        // Assert
        Assert.Equal(timeout, exception.Timeout);
        Assert.Equal($"Operation '{operation}' timed out after {timeout.TotalSeconds} seconds.", exception.Message);
    }

    [Theory]
    [InlineData("API call", 5)]
    [InlineData("File upload", 60)]
    [InlineData("Background job", 300)]
    [InlineData("Cache refresh", 0.5)]
    public void ShouldFormatMessagesCorrectly_WhenUsingTimeoutExceptionWithVariousTimeouts(string operation, double seconds)
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(seconds);

        // Act
        var exception = new Orkeon.Domain.Exceptions.TimeoutException(operation, timeout);

        // Assert
        Assert.Equal(timeout, exception.Timeout);
        Assert.Contains(operation, exception.Message);
        Assert.Contains(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message);
    }

    [Fact]
    public void ShouldShowSeconds_WhenUsingTimeoutExceptionWithMillisecondTimeout()
    {
        // Arrange
        var operation = "Quick operation";
        var timeout = TimeSpan.FromMilliseconds(500);

        // Act
        var exception = new Orkeon.Domain.Exceptions.TimeoutException(operation, timeout);

        // Assert
        Assert.Equal(timeout, exception.Timeout);
        Assert.Contains("0.5", exception.Message); // 500ms = 0.5 seconds
    }

    #endregion

    #region Exception Hierarchy Tests

    [Fact]
    public void ShouldInheritFromDomainException_WhenUsingAllDomainExceptions()
    {
        // Arrange & Act
        var agentException = new AgentException(AgentId.Create(), "Agent error");
        var taskException = new TaskException(TaskId.Create(), "Task error");
        var crewException = new CrewException(CrewId.Create(), "Crew error");
        var toolException = new ToolExecutionException("tool", "message");
        var memoryException = new MemoryException("message");
        var validationException = new ValidationException("prop", "message");
        var notFoundException = new NotFoundException("type", "id");
        var timeoutException = new Orkeon.Domain.Exceptions.TimeoutException("op", TimeSpan.FromSeconds(1));

        // Assert - verify each exception is the expected concrete type (which inherits from DomainException)
        Assert.IsType<AgentException>(agentException);
        Assert.IsType<TaskException>(taskException);
        Assert.IsType<CrewException>(crewException);
        Assert.IsType<ToolExecutionException>(toolException);
        Assert.IsType<MemoryException>(memoryException);
        Assert.IsType<ValidationException>(validationException);
        Assert.IsType<NotFoundException>(notFoundException);
        Assert.IsType<Orkeon.Domain.Exceptions.TimeoutException>(timeoutException);
    }

    [Fact]
    public void ShouldInheritFromSystemException_WhenUsingAllDomainExceptions()
    {
        // Arrange
        var exceptions = new List<Exception>
        {
            new AgentException(AgentId.Create(), "Agent error"),
            new TaskException(TaskId.Create(), "Task error"),
            new CrewException(CrewId.Create(), "Crew error"),
            new ToolExecutionException("tool", "message"),
            new MemoryException("message"),
            new ValidationException("prop", "message"),
            new NotFoundException("type", "id"),
            new Orkeon.Domain.Exceptions.TimeoutException("op", TimeSpan.FromSeconds(1))
        };

        // Assert
        Assert.All(exceptions, ex => Assert.NotNull(ex));
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleGracefully_WhenUsingDomainExceptionsWithNullMessages()
    {
        // Act & Assert - C# will use default message for null
        var agentEx = new AgentException(AgentId.Create(), null!);
        var taskEx = new TaskException(TaskId.Create(), null!);
        var crewEx = new CrewException(CrewId.Create(), null!);
        var memoryEx = new MemoryException(null!);

        Assert.NotNull(agentEx.Message);
        Assert.NotNull(taskEx.Message);
        Assert.NotNull(crewEx.Message);
        Assert.NotNull(memoryEx.Message);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingDomainExceptionsWithUnicodeContent()
    {
        // Arrange & Act
        var agentEx = new AgentException(AgentId.Create(), "Unicode agent error ❌");
        var validationEx = new ValidationException("用户名", "包含非法字符 ⚠️");
        var notFoundEx = new NotFoundException("用户", "用户-456 🔍");
        var timeoutEx = new Orkeon.Domain.Exceptions.TimeoutException("数据库查询 🗄️", TimeoutQuick);

        // Assert
        Assert.NotNull(agentEx.AgentId);
        Assert.Contains("Unicode agent error", agentEx.Message);
        Assert.Contains("用户名", validationEx.PropertyName);
        Assert.Contains("⚠️", validationEx.Message);
        Assert.Contains("🔍", notFoundEx.ResourceId);
        Assert.Contains("🗄️", timeoutEx.Message);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTimeoutExceptionWithZeroTimeout()
    {
        // Arrange & Act
        var exception = new Orkeon.Domain.Exceptions.TimeoutException("Instant operation", TimeSpan.Zero);

        // Assert
        Assert.Equal(TimeSpan.Zero, exception.Timeout);
        Assert.Contains("0 seconds", exception.Message);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTimeoutExceptionWithNegativeTimeout()
    {
        // Arrange & Act
        var exception = new Orkeon.Domain.Exceptions.TimeoutException("Strange operation", TimeSpan.FromSeconds(-5));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(-5), exception.Timeout);
        Assert.Contains("-5 seconds", exception.Message);
    }

    #endregion

    #region Exception Throwing and Catching Scenarios

    [Fact]
    public void ShouldCatchSpecificException_WhenUsingExceptionScenario()
    {
        // Arrange
        void ThrowAgentException()
        {
            throw new AgentException(AgentId.Create(), "Agent error");
        }

        // Act & Assert
        var exception = Assert.Throws<AgentException>(() => ThrowAgentException());
        Assert.Equal("Agent error", exception.Message);
    }

    [Fact]
    public void ShouldCatchAsDomainException_WhenUsingExceptionScenario()
    {
        // Arrange
        void ThrowVariousExceptions(int scenario)
        {
            switch (scenario)
            {
                case 1:
                    throw new AgentException(AgentId.Create(), "Error scenario 1");
                case 2:
                    throw new TaskException(TaskId.Create(), "Error scenario 2");
                case 3:
                    throw new ValidationException("field", "Error 3");
                default:
                    throw new MemoryException("Error 4");
            }
        }

        // Act & Assert
        for (int i = 1; i <= 4; i++)
        {
            try
            {
                ThrowVariousExceptions(i);
                Assert.Fail($"Expected exception to be thrown for scenario {i}");
            }
            catch (DomainException exception)
            {
                Assert.NotNull(exception);
                Assert.Contains("Error", exception.Message);
            }
        }
    }

    [Fact]
    public void ShouldChainedExceptions_WhenUsingExceptionScenario()
    {
        // Arrange
        Exception? caughtException = null;

        try
        {
            try
            {
                try
                {
                    // Simulate deep call stack
                    throw new IOException("Disk error");
                }
                catch (IOException ioEx)
                {
                    throw new MemoryException("Failed to save to memory", ioEx);
                }
            }
            catch (MemoryException memEx)
            {
                throw new TaskException(TaskId.Create(), "Task failed", memEx);
            }
        }
        catch (TaskException taskEx)
        {
            caughtException = taskEx;
        }

        // Assert
        Assert.NotNull(caughtException);
        var taskException = caughtException as TaskException;
        Assert.NotNull(taskException);
        Assert.NotNull(taskException!.TaskId);

        var memoryException = taskException.InnerException as MemoryException;
        Assert.NotNull(memoryException);

        var ioException = memoryException!.InnerException as IOException;
        Assert.NotNull(ioException);
        Assert.Equal("Disk error", ioException!.Message);
    }

    #endregion
}
