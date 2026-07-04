using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Task;

public class ICrewTaskTests
{
    // Test implementation of ICrewTask
    private class TestCrewTask : ICrewTask
    {
        public TaskId TaskId { get; set; }
        public TaskDescription Description { get; set; }
        public ExpectedOutput ExpectedOutput { get; set; }
        public AgentId? AssignedAgent { get; set; }
        public TaskStatus Status { get; set; }
        public TaskOutput? Output { get; set; }
        private readonly List<TaskId> _dependencies = [];
        public IReadOnlyList<TaskId> Dependencies => _dependencies.AsReadOnly();
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool AsyncExecution { get; set; }
        public JsonSchema? OutputJson { get; set; }
        public Type? OutputPydantic { get; set; }
        public string? OutputFile { get; set; }
        public bool HumanInput { get; set; }

        public TestCrewTask(string description = "Test task", string expectedOutput = "Expected output")
        {
            TaskId = TaskId.From(Guid.NewGuid());
            Description = TaskDescription.From(description);
            ExpectedOutput = ExpectedOutput.From(expectedOutput);
            Status = TaskStatus.Pending;
            CreatedAt = DateTime.UtcNow;
            AsyncExecution = false;
            HumanInput = false;
        }

        public void AddDependency(TaskId dependency)
        {
            _dependencies.Add(dependency);
        }

        public void AssignTo(AgentId agentId)
        {
            if (Status != TaskStatus.Pending)
                throw new InvalidOperationException("Can only assign pending tasks");
            AssignedAgent = agentId;
        }

        public void Start(AgentId agentId)
        {
            if (Status != TaskStatus.Pending)
                throw new InvalidOperationException("Can only start pending tasks");
            if (AssignedAgent == null)
                AssignedAgent = agentId;
            Status = TaskStatus.InProgress;
            StartedAt = DateTime.UtcNow;
        }

        public void Complete(AgentId agentId, TaskOutput output)
        {
            if (Status != TaskStatus.InProgress)
                throw new InvalidOperationException("Can only complete in-progress tasks");
            Status = TaskStatus.Completed;
            Output = output;
            CompletedAt = DateTime.UtcNow;
        }

        public void Fail(string errorMessage, Exception? exception = null)
        {
            Status = TaskStatus.Failed;
            Output = TaskOutput.Create(errorMessage, "error", "text");
            CompletedAt = DateTime.UtcNow;
        }

        public bool CanExecute(Func<TaskId, bool> isTaskCompleted)
        {
            return Dependencies.All(isTaskCompleted);
        }

        public ValidationResult ValidateOutput(TaskOutput output)
        {
            var errors = new List<string>();

            if (output == null)
                errors.Add("Output cannot be null");
            else if (string.IsNullOrWhiteSpace(output.RawOutput))
                errors.Add("Output content cannot be empty");

            if (OutputJson != null && output?.Format != "json")
                errors.Add("Output must be in JSON format");

            var validationErrors = errors.Select(e => new Orkeon.Domain.SharedKernel.ValidationError(nameof(Output), e)).ToArray();
            return new ValidationResult(validationErrors);
        }

        public string GetContextSummary()
        {
            return $"Task: {Description.Value}, Status: {Status}, Agent: {AssignedAgent?.ToString() ?? "Unassigned"}";
        }

        public TimeSpan GetExecutionTime()
        {
            if (StartedAt == null)
                return TimeSpan.Zero;

            var endTime = CompletedAt ?? DateTime.UtcNow;
            return endTime - StartedAt.Value;
        }
    }

    [Fact]
    public void ShouldInitializeWithCorrectDefaults_WhenUsingICrewTask()
    {
        // Arrange & Act
        var task = new TestCrewTask();

        // Assert
        Assert.NotNull(task.TaskId);
        Assert.NotNull(task.Description);
        Assert.Equal("Test task", task.Description.Value);
        Assert.Equal("Expected output", task.ExpectedOutput);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(task.AssignedAgent);
        Assert.Null(task.Output);
        Assert.Empty(task.Dependencies);
        Assert.True(task.CreatedAt <= DateTime.UtcNow);
        Assert.Null(task.StartedAt);
        Assert.Null(task.CompletedAt);
        Assert.False(task.AsyncExecution);
        Assert.False(task.HumanInput);
    }

    [Fact]
    public void ShouldAssignAgent_WhenAssigningToWithPendingTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        task.AssignTo(agentId);

        // Assert
        Assert.Equal(agentId, task.AssignedAgent);
        Assert.Equal(TaskStatus.Pending, task.Status);
    }

    [Fact]
    public void ShouldThrowException_WhenAssigningToWithNonPendingTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());
        task.Start(agentId);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => task.AssignTo(AgentId.From(Guid.NewGuid())));
        Assert.Contains("only assign pending tasks", exception.Message);
    }

    [Fact]
    public void ShouldStartTask_WhenStartingWithPendingTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        task.Start(agentId);

        // Assert
        Assert.Equal(TaskStatus.InProgress, task.Status);
        Assert.Equal(agentId, task.AssignedAgent);
        Assert.NotNull(task.StartedAt);
        Assert.True(task.StartedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrowException_WhenStartingWithAlreadyStartedTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());
        task.Start(agentId);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => task.Start(agentId));
        Assert.Contains("only start pending tasks", exception.Message);
    }

    [Fact]
    public void ShouldCompleteTask_WhenCompletingWithInProgressTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());
        var output = TaskOutput.Create("Task completed successfully", "result", "json");
        task.Start(agentId);

        // Act
        task.Complete(agentId, output);

        // Assert
        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.Equal(output, task.Output);
        Assert.NotNull(task.CompletedAt);
        Assert.True(task.CompletedAt > task.StartedAt);
    }

    [Fact]
    public void ShouldThrowException_WhenCompletingWithPendingTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());
        var output = TaskOutput.Create("Result", "raw", "json");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => task.Complete(agentId, output));
        Assert.Contains("only complete in-progress tasks", exception.Message);
    }

    [Fact]
    public void ShouldSetTaskToFailed_WhenFailing()
    {
        // Arrange
        var task = new TestCrewTask();
        var errorMessage = "Task execution failed";
        var exception = new InvalidOperationException("Internal error");

        // Act
        task.Fail(errorMessage, exception);

        // Assert
        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.NotNull(task.Output);
        Assert.Equal(errorMessage, task.Output.RawOutput);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingCanExecuteWithNoDependencies()
    {
        // Arrange
        var task = new TestCrewTask();

        // Act
        var canExecute = task.CanExecute(id => true);

        // Assert
        Assert.True(canExecute);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingCanExecuteWithCompletedDependencies()
    {
        // Arrange
        var task = new TestCrewTask();
        var dep1 = TaskId.From(Guid.NewGuid());
        var dep2 = TaskId.From(Guid.NewGuid());
        task.AddDependency(dep1);
        task.AddDependency(dep2);

        var completedTasks = new HashSet<TaskId> { dep1, dep2 };

        // Act
        var canExecute = task.CanExecute(id => completedTasks.Contains(id));

        // Assert
        Assert.True(canExecute);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingCanExecuteWithIncompleteDependencies()
    {
        // Arrange
        var task = new TestCrewTask();
        var dep1 = TaskId.From(Guid.NewGuid());
        var dep2 = TaskId.From(Guid.NewGuid());
        task.AddDependency(dep1);
        task.AddDependency(dep2);

        var completedTasks = new HashSet<TaskId> { dep1 }; // Only dep1 is completed

        // Act
        var canExecute = task.CanExecute(id => completedTasks.Contains(id));

        // Assert
        Assert.False(canExecute);
    }

    [Fact]
    public void ShouldReturnValid_WhenUsingValidateOutputWithValidOutput()
    {
        // Arrange
        var task = new TestCrewTask();
        var output = TaskOutput.Create("Valid output", "raw", "text");

        // Act
        var result = task.ValidateOutput(output);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldReturnInvalid_WhenUsingValidateOutputWithNullOutput()
    {
        // Arrange
        var task = new TestCrewTask();

        // Act
        var result = task.ValidateOutput(null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Output cannot be null");
    }

    [Fact]
    public void ShouldReturnInvalid_WhenUsingValidateOutputWithJsonSchemaButWrongFormat()
    {
        // Arrange
        var task = new TestCrewTask
        {
            OutputJson = JsonSchema.From("{\"type\": \"object\"}")
        };
        var output = TaskOutput.Create("Not JSON", "text");

        // Act
        var result = task.ValidateOutput(output);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Output must be in JSON format");
    }

    [Fact]
    public void ShouldReturnCorrectSummary_WhenGettingContextSummary()
    {
        // Arrange
        var task = new TestCrewTask(GoalAnalyzeData, "Analysis report");
        var agentId = AgentId.From(Guid.NewGuid());
        task.AssignTo(agentId);
        task.Start(agentId);

        // Act
        var summary = task.GetContextSummary();

        // Assert
        Assert.Contains(GoalAnalyzeData, summary);
        Assert.Contains(InProgress, summary);
        Assert.Contains(agentId.ToString(), summary);
    }

    [Fact]
    public void ShouldReturnZero_WhenGettingExecutionTimeWithNotStartedTask()
    {
        // Arrange
        var task = new TestCrewTask();

        // Act
        var executionTime = task.GetExecutionTime();

        // Assert
        Assert.Equal(TimeSpan.Zero, executionTime);
    }

    [Fact]
    public void ShouldReturnElapsedTime_WhenGettingExecutionTimeWithInProgressTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());
        task.Start(agentId);
        // Wait >= 100 ms on the same clock GetExecutionTime uses (deterministic, R5.6)
        ClockAdvance.Until(() => DateTime.UtcNow - task.StartedAt!.Value >= TimeSpan.FromMilliseconds(100));

        // Act
        var executionTime = task.GetExecutionTime();

        // Assert
        Assert.True(executionTime.TotalMilliseconds >= 100);
    }

    [Fact]
    public void ShouldReturnTotalTime_WhenGettingExecutionTimeWithCompletedTask()
    {
        // Arrange
        var task = new TestCrewTask();
        var agentId = AgentId.From(Guid.NewGuid());
        task.Start(agentId);
        // Wait >= 50 ms on the same clock GetExecutionTime uses (deterministic, R5.6)
        ClockAdvance.Until(() => DateTime.UtcNow - task.StartedAt!.Value >= TimeSpan.FromMilliseconds(50));
        task.Complete(agentId, TaskOutput.Create("Done", "raw", "text"));

        // Act
        var executionTime = task.GetExecutionTime();

        // Assert
        Assert.True(executionTime.TotalMilliseconds >= 50);
        Assert.Equal(task.CompletedAt!.Value - task.StartedAt!.Value, executionTime);
    }

    [Fact]
    public void ShouldSetFlag_WhenUsingICrewTaskWithAsyncExecution()
    {
        // Arrange & Act
        var task = new TestCrewTask
        {
            AsyncExecution = true
        };

        // Assert
        Assert.True(task.AsyncExecution);
    }

    [Fact]
    public void ShouldSetFlag_WhenUsingICrewTaskWithHumanInput()
    {
        // Arrange & Act
        var task = new TestCrewTask
        {
            HumanInput = true
        };

        // Assert
        Assert.True(task.HumanInput);
    }

    [Fact]
    public void ShouldSetPath_WhenUsingICrewTaskWithOutputFile()
    {
        // Arrange & Act
        var task = new TestCrewTask
        {
            OutputFile = "/path/to/output.txt"
        };

        // Assert
        Assert.Equal("/path/to/output.txt", task.OutputFile);
    }
}
