using Orkeon.Domain.Common;
using Orkeon.Domain.Task.Events;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Domain.Tests.Entities;

/// <summary>
/// Tests for Task entity following Clean Architecture principles.
/// Tests the business rules and domain logic of the Task aggregate root.
/// </summary>
public class TaskTests
{
    [Fact]
    public void ShouldCreateTask_WhenCreatingWithValidParameters()
    {
        // Arrange
        var description = TaskDescription.From("Implement user authentication");
        var expectedOutput = ExpectedOutput.From("Working login and registration system");

        // Act
        var task = DomainTask.Create(description, expectedOutput);

        // Assert
        Assert.NotNull(task);
        Assert.NotEqual<object>(Guid.Empty, task.Id.Value);
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.False(task.AsyncExecution);
        Assert.False(task.HumanInput);
        Assert.True(task.CreatedAt <= DateTime.UtcNow);
        Assert.True(task.CreatedAt >= DateTime.UtcNow.AddSeconds(-1));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreatingWithNullDescription()
    {
        // Arrange
        var expectedOutput = ExpectedOutput.From("Valid output");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => DomainTask.Create(null!, expectedOutput)
        );
        Assert.Equal("description", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShouldThrowArgumentException_WhenCreatingWithEmptyOrNullExpectedOutput(string? expectedOutput)
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => ExpectedOutput.From(expectedOutput!)
        );
        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Normal")]
    [InlineData("High")]
    [InlineData("Critical")]
    public void ShouldSetPriority_WhenCreatingWithDifferentPriorities(string priorityStr)
    {
        // Arrange
        var priority = TaskPriority.From(priorityStr);
        var description = TaskDescription.From("Test priority levels");
        var expectedOutput = ExpectedOutput.From("Completed task with correct priority");

        // Act
        var task = DomainTask.Create(description, expectedOutput, priority);

        // Assert
        Assert.Equal(priority, task.Priority);
    }

    [Fact]
    public void ShouldSetAsyncFlag_WhenCreatingWithAsyncExecution()
    {
        // Arrange
        var description = TaskDescription.From("Async task");
        var expectedOutput = ExpectedOutput.From("Async execution result");

        // Act
        var task = DomainTask.Create(description, expectedOutput, outputOptions: new TaskOutputOptions { AsyncExecution = true });

        // Assert
        Assert.True(task.AsyncExecution);
    }

    [Fact]
    public void ShouldSetHumanInputFlag_WhenCreatingWithHumanInput()
    {
        // Arrange
        var description = TaskDescription.From("Human input task");
        var expectedOutput = ExpectedOutput.From("Result with human input");

        // Act
        var task = DomainTask.Create(description, expectedOutput, outputOptions: new TaskOutputOptions { HumanInput = true });

        // Assert
        Assert.True(task.HumanInput);
    }

    [Fact]
    public void ShouldSetAllParameters_WhenCreatingWithCustomParameters()
    {
        // Arrange
        var description = TaskDescription.From("Complex task with all parameters");
        var expectedOutput = ExpectedOutput.From("Comprehensive output");
        var priority = TaskPriority.High;
        var asyncExecution = true;
        var outputFile = "/output/result.json";
        var humanInput = true;

        // Act
        var task = DomainTask.Create(
            description: description,
            expectedOutput: expectedOutput,
            priority: priority,
            outputOptions: new TaskOutputOptions
            {
                AsyncExecution = asyncExecution,
                OutputFile = outputFile,
                HumanInput = humanInput
            }
        );

        // Assert
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.Equal(priority, task.Priority);
        Assert.Equal(asyncExecution, task.AsyncExecution);
        Assert.Equal(outputFile, task.OutputFile);
        Assert.Equal(humanInput, task.HumanInput);
    }

    [Fact]
    public void ShouldGenerateUniqueTaskIds_WhenCreating()
    {
        // Arrange
        var description = TaskDescription.From("Test unique IDs");
        var expectedOutput = ExpectedOutput.From("Different IDs for each task");

        // Act
        var task1 = DomainTask.Create(description, expectedOutput);
        var task2 = DomainTask.Create(description, expectedOutput);

        // Assert
        Assert.NotEqual(task1.Id.Value, task2.Id.Value);
    }

    [Fact]
    public void ShouldRaiseTaskCreatedEvent_WhenCreating()
    {
        // Arrange
        var description = TaskDescription.From("Event test task");
        var expectedOutput = ExpectedOutput.From("Task creation event");

        // Act
        var task = DomainTask.Create(description, expectedOutput);

        // Assert
        var events = task.DomainEvents;
        Assert.Single(events);

        var taskCreatedEvent = Assert.IsType<TaskCreatedEvent>(events[0]);
        Assert.Equal(task.Id, taskCreatedEvent.TaskId);
        Assert.Equal(description, taskCreatedEvent.Description);
        Assert.True(taskCreatedEvent.OccurredAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldReturnTaskIdAsString_WhenUsingIdProperty()
    {
        // Arrange
        var description = TaskDescription.From("ID test");
        var expectedOutput = ExpectedOutput.From("String ID representation");
        var task = DomainTask.Create(description, expectedOutput);

        // Act
        var id = task.Id;
        var stringId = task.Id.ToString();

        // Assert
        Assert.NotNull(id);
        Assert.NotEmpty(stringId);
        // StringId should contain the GUID value
        Assert.Contains(task.Id.Value.ToString(), stringId);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingDependenciesCollection()
    {
        // Arrange
        var description = TaskDescription.From("Dependencies test");
        var expectedOutput = ExpectedOutput.From("Read-only dependencies");
        var task = DomainTask.Create(description, expectedOutput);

        // Act
        var dependencies = task.Dependencies;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<TaskId>>(dependencies);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingRequiredToolsCollection()
    {
        // Arrange
        var description = TaskDescription.From("Tools test");
        var expectedOutput = ExpectedOutput.From("Read-only tools");
        var task = DomainTask.Create(description, expectedOutput);

        // Act
        var tools = task.RequiredTools;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<ToolId>>(tools);
    }

    [Fact]
    public void ShouldBeNull_WhenUsingAssignedAgentUsingInitially()
    {
        // Arrange
        var description = TaskDescription.From("Assignment test");
        var expectedOutput = ExpectedOutput.From("No initial assignment");
        var task = DomainTask.Create(description, expectedOutput);

        // Act & Assert
        Assert.Null(task.AssignedAgent);
    }

    [Fact]
    public void ShouldBeNull_WhenUsingOutputInitially()
    {
        // Arrange
        var description = TaskDescription.From("Output test");
        var expectedOutput = ExpectedOutput.From("No initial output");
        var task = DomainTask.Create(description, expectedOutput);

        // Act & Assert
        Assert.Null(task.Output);
    }

    [Fact]
    public void ShouldNotBeNull_WhenUsingContext()
    {
        // Arrange
        var description = TaskDescription.From("Context test");
        var expectedOutput = ExpectedOutput.From("Valid context");
        var task = DomainTask.Create(description, expectedOutput);

        // Act & Assert
        Assert.NotNull(task.Context);
    }

    [Fact]
    public void ShouldBeNull_WhenUsingStartedAtInitially()
    {
        // Arrange
        var description = TaskDescription.From("Start time test");
        var expectedOutput = ExpectedOutput.From("No start time initially");
        var task = DomainTask.Create(description, expectedOutput);

        // Act & Assert
        Assert.Null(task.StartedAt);
    }

    [Fact]
    public void ShouldBeNull_WhenUsingCompletedAtInitially()
    {
        // Arrange
        var description = TaskDescription.From("Completion time test");
        var expectedOutput = ExpectedOutput.From("No completion time initially");
        var task = DomainTask.Create(description, expectedOutput);

        // Act & Assert
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void ShouldSetOutputFile_WhenCreatingWithOutputFile()
    {
        // Arrange
        var description = TaskDescription.From("Output file test");
        var expectedOutput = ExpectedOutput.From("File output");
        var outputFile = "/path/to/output.txt";

        // Act
        var task = DomainTask.Create(description, expectedOutput, outputOptions: new TaskOutputOptions { OutputFile = outputFile });

        // Assert
        Assert.Equal(outputFile, task.OutputFile);
    }

    [Theory]
    [InlineData("Low", "Normal")]
    [InlineData("Normal", "High")]
    [InlineData("High", "Critical")]
    public void ShouldCanBeCompared_WhenUsingPriority(string lowerStr, string higherStr)
    {
        // Arrange
        var lower = TaskPriority.From(lowerStr);
        var higher = TaskPriority.From(higherStr);
        var description = TaskDescription.From("Priority comparison");
        var expectedOutput = ExpectedOutput.From("Comparable priorities");

        var lowerTask = DomainTask.Create(description, expectedOutput, lower);
        var higherTask = DomainTask.Create(description, expectedOutput, higher);

        // Act & Assert
        Assert.True(lowerTask.Priority.Order < higherTask.Priority.Order);
    }
}
