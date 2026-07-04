using System.Collections.Immutable;
using Orkeon.Application.Task.DTOs;
using Orkeon.Application.Common.DTOs;
using TaskOutputDto = Orkeon.Application.Task.DTOs.TaskOutputDto;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.DTOs.Tasks;

public class TaskDtoTests
{
    private static readonly string[] s_deps12 = [TaskId1, TaskId2];
    private static readonly string[] s_tools12 = ["FileReader", "DataProcessor"];
    private static readonly string[] s_depsAB = ["task-a", "task-b"];
    private static readonly string[] s_tools123 = ["Tool1", "Tool2", "Tool3"];
    private static readonly string[] s_allTools = ["FileReadTool", "WebScrapeTool", "DataProcessorTool", "EmailSenderTool"];

    [Fact]
    public void ShouldCreateValidDto_WhenConstructingWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new TaskDto
        {
            Id = "task-123",
            Name = "Test Task",
            Description = "Perform testing operations",
            ExpectedOutput = "Test results",
            Status = Pending,
            Priority = "Medium"
        };

        // Assert
        Assert.Equal("task-123", dto.Id);
        Assert.Equal("Test Task", dto.Name);
        Assert.Equal("Perform testing operations", dto.Description);
        Assert.Equal("Test results", dto.ExpectedOutput);
        Assert.Equal(Pending, dto.Status);
        Assert.Equal("Medium", dto.Priority);
        Assert.Null(dto.AssignedAgent);
        Assert.False(dto.IsAsync);
        Assert.Equal(TimeoutLong, dto.EstimatedDuration);
        Assert.Null(dto.ActualDuration);
        Assert.True(dto.Dependencies.IsEmpty);
        Assert.True(dto.RequiredTools.IsEmpty);
        Assert.Null(dto.Output);
        Assert.Null(dto.Complexity);
        Assert.Null(dto.ExecutionPlan);
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
        Assert.Null(dto.StartedAt);
        Assert.Null(dto.CompletedAt);
        Assert.True(dto.Context.IsEmpty);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenConstructingWithAllProperties()
    {
        // Arrange
        var dependencies = ImmutableList<string>.Empty.AddRange(s_deps12);
        var requiredTools = ImmutableList<string>.Empty.AddRange(s_tools12);
        var context = ImmutableDictionary<string, object>.Empty
            .Add("environment", "production")
            .Add("retryCount", 0);

        var output = new TaskOutputDto
        {
            RawOutput = "Task completed",
            Format = "text"
        };

        var complexity = new TaskComplexityDto
        {
            Level = "High",
            EstimatedEffort = 8,
            ComplexityScore = 7.5
        };

        var executionPlan = new ExecutionPlanDto
        {
            Id = "plan-123",
            Name = "Test Plan",
            Strategy = "Sequential",
            Status = Active
        };

        var createdAt = DateTime.UtcNow.AddHours(-2);
        var startedAt = DateTime.UtcNow.AddHours(-1);
        var completedAt = DateTime.UtcNow.AddMinutes(-30);

        // Act
        var dto = new TaskDto
        {
            Id = "task-456",
            Name = "Complex Task",
            Description = "Handle complex operations",
            ExpectedOutput = "Detailed results",
            AssignedAgent = "agent-789",
            Status = Completed,
            Priority = "High",
            IsAsync = true,
            EstimatedDuration = TimeSpan.FromHours(2),
            ActualDuration = TimeSpan.FromMinutes(90),
            Dependencies = dependencies,
            RequiredTools = requiredTools,
            Output = output,
            Complexity = complexity,
            ExecutionPlan = executionPlan,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Context = context
        };

        // Assert all properties
        Assert.Equal("task-456", dto.Id);
        Assert.Equal("Complex Task", dto.Name);
        Assert.Equal("Handle complex operations", dto.Description);
        Assert.Equal("Detailed results", dto.ExpectedOutput);
        Assert.Equal("agent-789", dto.AssignedAgent);
        Assert.Equal(Completed, dto.Status);
        Assert.Equal("High", dto.Priority);
        Assert.True(dto.IsAsync);
        Assert.Equal(TimeSpan.FromHours(2), dto.EstimatedDuration);
        Assert.Equal(TimeSpan.FromMinutes(90), dto.ActualDuration);
        Assert.Equal(2, dto.Dependencies.Count);
        Assert.Contains(TaskId1, dto.Dependencies);
        Assert.Contains(TaskId2, dto.Dependencies);
        Assert.Equal(2, dto.RequiredTools.Count);
        Assert.Contains("FileReader", dto.RequiredTools);
        Assert.Contains("DataProcessor", dto.RequiredTools);
        Assert.NotNull(dto.Output);
        Assert.NotNull(dto.Complexity);
        Assert.NotNull(dto.ExecutionPlan);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Equal(startedAt, dto.StartedAt);
        Assert.Equal(completedAt, dto.CompletedAt);
        Assert.Equal(2, dto.Context.Count);
    }

    [Fact]
    public void ShouldSetStartedAt_WhenUsingWithStatusInProgress()
    {
        // Arrange
        var originalDto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium"
        };

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var updatedDto = originalDto.WithStatus(InProgress);
        var afterUpdate = DateTime.UtcNow;

        // Assert
        Assert.Equal(Pending, originalDto.Status);
        Assert.Null(originalDto.StartedAt);
        Assert.Equal(InProgress, updatedDto.Status);
        Assert.NotNull(updatedDto.StartedAt);
        Assert.True(beforeUpdate <= updatedDto.StartedAt);
        Assert.True(updatedDto.StartedAt <= afterUpdate);
        Assert.Null(updatedDto.CompletedAt);
        Assert.NotSame(originalDto, updatedDto);
    }

    [Fact]
    public void ShouldSetCompletedAt_WhenUsingWithStatusUsingCompleted()
    {
        // Arrange
        var originalDto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = InProgress,
            Priority = "Medium",
            StartedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var updatedDto = originalDto.WithStatus(Completed);
        var afterUpdate = DateTime.UtcNow;

        // Assert
        Assert.Equal(InProgress, originalDto.Status);
        Assert.Null(originalDto.CompletedAt);
        Assert.Equal(Completed, updatedDto.Status);
        Assert.NotNull(updatedDto.CompletedAt);
        Assert.True(beforeUpdate <= updatedDto.CompletedAt);
        Assert.True(updatedDto.CompletedAt <= afterUpdate);
        Assert.NotSame(originalDto, updatedDto);
    }

    [Fact]
    public void ShouldSetCompletedAt_WhenUsingWithStatusUsingFailed()
    {
        // Arrange
        var originalDto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = InProgress,
            Priority = "Medium",
            StartedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var updatedDto = originalDto.WithStatus(Failed);
        var afterUpdate = DateTime.UtcNow;

        // Assert
        Assert.Equal(Failed, updatedDto.Status);
        Assert.NotNull(updatedDto.CompletedAt);
        Assert.True(beforeUpdate <= updatedDto.CompletedAt);
        Assert.True(updatedDto.CompletedAt <= afterUpdate);
    }

    [Fact]
    public void ShouldOnlyUpdateStatus_WhenUsingWithStatusWithOtherStatus()
    {
        // Arrange
        var originalDto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium"
        };

        // Act
        var updatedDto = originalDto.WithStatus("Queued");

        // Assert
        Assert.Equal("Queued", updatedDto.Status);
        Assert.Null(updatedDto.StartedAt);
        Assert.Null(updatedDto.CompletedAt);
    }

    [Fact]
    public void ShouldSetOutputAndMarkCompleted_WhenUsingWithOutput()
    {
        // Arrange
        var originalDto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = InProgress,
            Priority = "Medium",
            StartedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var output = new TaskOutputDto
        {
            RawOutput = "Task completed successfully",
            Format = "text",
            SizeBytes = 256
        };

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var updatedDto = originalDto.WithOutput(output);
        var afterUpdate = DateTime.UtcNow;

        // Assert
        Assert.Null(originalDto.Output);
        Assert.Equal(InProgress, originalDto.Status);
        Assert.NotNull(updatedDto.Output);
        Assert.Equal(output, updatedDto.Output);
        Assert.Equal(Completed, updatedDto.Status);
        Assert.NotNull(updatedDto.CompletedAt);
        Assert.True(beforeUpdate <= updatedDto.CompletedAt);
        Assert.True(updatedDto.CompletedAt <= afterUpdate);
        Assert.NotSame(originalDto, updatedDto);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Critical")]
    public void ShouldBeSettable_WhenUsingPriorityWithDifferentLevels(string priority)
    {
        // Arrange & Act
        var dto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = priority
        };

        // Assert
        Assert.Equal(priority, dto.Priority);
    }

    [Fact]
    public void ShouldAcceptImmutableList_WhenUsingDependencies()
    {
        // Arrange
        var dependencies = ImmutableList<string>.Empty
            .Add("task-001")
            .Add("task-002")
            .Add("task-003")
            .Add("task-004");

        // Act
        var dto = new TaskDto
        {
            Id = "task-005",
            Name = "Dependent Task",
            Description = "Task with dependencies",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium",
            Dependencies = dependencies
        };

        // Assert
        Assert.Equal(4, dto.Dependencies.Count);
        Assert.Equal("task-001", dto.Dependencies[0]);
        Assert.Equal("task-002", dto.Dependencies[1]);
        Assert.Equal("task-003", dto.Dependencies[2]);
        Assert.Equal("task-004", dto.Dependencies[3]);
    }

    [Fact]
    public void ShouldAcceptImmutableList_WhenUsingRequiredTools()
    {
        // Arrange
        var tools = ImmutableList<string>.Empty
            .Add("FileReadTool")
            .Add("WebScrapeTool")
            .Add("DataProcessorTool")
            .Add("EmailSenderTool");

        // Act
        var dto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium",
            RequiredTools = tools
        };

        // Assert
        Assert.Equal(4, dto.RequiredTools.Count);
        foreach (var tool in s_allTools)
        {
            Assert.Contains(tool, dto.RequiredTools);
        }
    }

    [Fact]
    public void ShouldAcceptImmutableDictionary_WhenUsingContext()
    {
        // Arrange
        var context = ImmutableDictionary<string, object>.Empty
            .Add("userId", "user-123")
            .Add("sessionId", "session-456")
            .Add("environment", "staging")
            .Add("debug", true)
            .Add("timeout", 3000);

        // Act
        var dto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium",
            Context = context
        };

        // Assert
        Assert.Equal(5, dto.Context.Count);
        Assert.Equal("user-123", dto.Context["userId"]);
        Assert.Equal("session-456", dto.Context["sessionId"]);
        Assert.Equal("staging", dto.Context["environment"]);
        Assert.True((bool)dto.Context["debug"]);
        Assert.Equal(3000, dto.Context["timeout"]);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingIsAsync()
    {
        // Arrange & Act
        var syncDto = new TaskDto
        {
            Id = TaskId1,
            Name = "Sync Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium",
            IsAsync = false
        };

        var asyncDto = new TaskDto
        {
            Id = TaskId2,
            Name = "Async Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium",
            IsAsync = true
        };

        // Assert
        Assert.False(syncDto.IsAsync);
        Assert.True(asyncDto.IsAsync);
    }

    [Fact]
    public void ShouldDefaultToFifteenMinutes_WhenUsingEstimatedDuration()
    {
        // Arrange & Act
        var dto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium"
        };

        // Assert
        Assert.Equal(TimeoutLong, dto.EstimatedDuration);
    }

    [Fact]
    public void ShouldBeOptional_WhenUsingActualDuration()
    {
        // Arrange & Act
        var dtoWithoutDuration = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "Medium"
        };

        var dtoWithDuration = new TaskDto
        {
            Id = TaskId2,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Completed,
            Priority = "Medium",
            ActualDuration = TimeSpan.FromMinutes(25)
        };

        // Assert
        Assert.Null(dtoWithoutDuration.ActualDuration);
        Assert.NotNull(dtoWithDuration.ActualDuration);
        Assert.Equal(TimeSpan.FromMinutes(25), dtoWithDuration.ActualDuration);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var id = "task-123";
        var name = "Test Task";
        var description = "Description";
        var expectedOutput = "Output";
        var status = Pending;
        var priority = "High";
        var createdAt = DateTime.UtcNow;

        // Act
        var dto1 = new TaskDto
        {
            Id = id,
            Name = name,
            Description = description,
            ExpectedOutput = expectedOutput,
            Status = status,
            Priority = priority,
            CreatedAt = createdAt
        };

        var dto2 = new TaskDto
        {
            Id = id,
            Name = name,
            Description = description,
            ExpectedOutput = expectedOutput,
            Status = status,
            Priority = priority,
            CreatedAt = createdAt
        };

        // Assert
        Assert.Equal(dto1, dto2);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange & Act
        var dto1 = new TaskDto
        {
            Id = TaskId1,
            Name = "Task 1",
            Description = "Description 1",
            ExpectedOutput = "Output 1",
            Status = Pending,
            Priority = "Low"
        };

        var dto2 = new TaskDto
        {
            Id = TaskId2,
            Name = "Task 2",
            Description = "Description 2",
            ExpectedOutput = "Output 2",
            Status = InProgress,
            Priority = "High"
        };

        // Assert
        Assert.NotEqual(dto1, dto2);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteTaskWithAllData()
    {
        // Arrange & Act
        var dto = new TaskDto
        {
            Id = "task-complete",
            Name = "Complete Task",
            Description = "A task with all properties filled",
            ExpectedOutput = "Comprehensive results",
            AssignedAgent = "agent-expert",
            Status = Completed,
            Priority = "Critical",
            IsAsync = true,
            EstimatedDuration = TimeSpan.FromHours(4),
            ActualDuration = TimeSpan.FromHours(3.5),
            Dependencies = ImmutableList<string>.Empty.AddRange(s_depsAB),
            RequiredTools = ImmutableList<string>.Empty.AddRange(s_tools123),
            Output = new TaskOutputDto { RawOutput = "Complete output", Format = "json" },
            Complexity = new TaskComplexityDto { Level = "Very High", ComplexityScore = 9.5 },
            ExecutionPlan = new ExecutionPlanDto { Id = "plan-1", Name = "Plan 1", Strategy = "Sequential", Status = "Executed" },
            CreatedAt = DateTime.UtcNow.AddHours(-4),
            StartedAt = DateTime.UtcNow.AddHours(-3.5),
            CompletedAt = DateTime.UtcNow,
            Context = ImmutableDictionary<string, object>.Empty.Add("complete", true)
        };

        // Assert
        Assert.Equal("task-complete", dto.Id);
        Assert.Equal("Complete Task", dto.Name);
        Assert.Equal("Critical", dto.Priority);
        Assert.True(dto.IsAsync);
        Assert.Equal(2, dto.Dependencies.Count);
        Assert.Equal(3, dto.RequiredTools.Count);
        Assert.NotNull(dto.Output);
        Assert.NotNull(dto.Complexity);
        Assert.NotNull(dto.ExecutionPlan);
        Assert.NotNull(dto.StartedAt);
        Assert.NotNull(dto.CompletedAt);
        Assert.Single(dto.Context);
    }
}
