using System.Collections.Immutable;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Application.Memory.DTOs;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Task.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.DTOs.Crews;

public class CrewDtoTests
{
    private static readonly string[] s_criticalAutomated = ["critical", "automated"];
    [Fact]
    public void ShouldCreateValidDto_WhenConstructingWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = "crew-123",
            Name = "Test Crew",
            Description = "A test crew for development",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        // Assert
        Assert.Equal("crew-123", dto.Id);
        Assert.Equal("Test Crew", dto.Name);
        Assert.Equal("A test crew for development", dto.Description);
        Assert.Equal("Sequential", dto.ProcessType);
        Assert.Equal("Ready", dto.Status);
        Assert.Equal("Normal", dto.Verbosity);
    }

    [Fact]
    public void ShouldBeSetCorrectly_WhenUsingDefaultValues()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Idle",
            Verbosity = "Normal"
        };

        // Assert
        Assert.False(dto.AllowCodeExecution);
        Assert.Equal(10, dto.MaxAgents);
        Assert.Equal(100, dto.MaxTasks);
        Assert.Equal(TimeSpan.FromMinutes(30), dto.ExecutionTimeout);
        Assert.True(dto.Agents.IsEmpty);
        Assert.True(dto.Tasks.IsEmpty);
        Assert.Null(dto.MemoryConfiguration);
        Assert.Null(dto.PerformanceMetrics);
        Assert.True(dto.ExecutionHistory.IsEmpty);
        Assert.Null(dto.StartedAt);
        Assert.Null(dto.CompletedAt);
        Assert.True(dto.Metadata.IsEmpty);
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldReturnCorrectCount_WhenUsingAgentCount()
    {
        // Arrange
        var agents = ImmutableList<AgentDto>.Empty
            .Add(new AgentDto
            {
                Id = AgentId1,
                Name = "Agent 1",
                Role = RoleWorker,
                Goal = GoalCompleteTasks,
                Backstory = "Worker agent",
                Type = RoleWorker,
                Status = "Ready"
            })
            .Add(new AgentDto
            {
                Id = AgentId2,
                Name = "Agent 2",
                Role = RoleManager,
                Goal = "Manage team",
                Backstory = "Manager agent",
                Type = RoleManager,
                Status = "Ready"
            });

        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Idle",
            Verbosity = "Normal",
            Agents = agents
        };

        // Act & Assert
        Assert.Equal(2, dto.AgentCount);
    }

    [Fact]
    public void ShouldReturnCorrectCount_WhenUsingTaskCount()
    {
        // Arrange
        var tasks = ImmutableList<TaskDto>.Empty
            .Add(new TaskDto
            {
                Id = TaskId1,
                Name = "Task 1",
                Description = "First task",
                ExpectedOutput = "Output 1",
                Status = Pending,
                Priority = "High"
            })
            .Add(new TaskDto
            {
                Id = TaskId2,
                Name = "Task 2",
                Description = "Second task",
                ExpectedOutput = "Output 2",
                Status = Pending,
                Priority = "Medium"
            })
            .Add(new TaskDto
            {
                Id = TaskId3,
                Name = "Task 3",
                Description = "Third task",
                ExpectedOutput = "Output 3",
                Status = Pending,
                Priority = "Low"
            });

        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Idle",
            Verbosity = "Normal",
            Tasks = tasks
        };

        // Act & Assert
        Assert.Equal(3, dto.TaskCount);
    }

    [Fact]
    public void ShouldSetStartedAt_WhenUsingWithStatusUsingExecuting()
    {
        // Arrange
        var originalDto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var updatedDto = originalDto.WithStatus("Executing");
        var afterUpdate = DateTime.UtcNow;

        // Assert
        Assert.Equal("Ready", originalDto.Status);
        Assert.Null(originalDto.StartedAt);
        Assert.Equal("Executing", updatedDto.Status);
        Assert.NotNull(updatedDto.StartedAt);
        Assert.True(beforeUpdate <= updatedDto.StartedAt);
        Assert.True(updatedDto.StartedAt <= afterUpdate);
        Assert.Null(updatedDto.CompletedAt);
    }

    [Fact]
    public void ShouldSetCompletedAt_WhenUsingWithStatusUsingCompleted()
    {
        // Arrange
        var originalDto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Executing",
            Verbosity = "Normal",
            StartedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var updatedDto = originalDto.WithStatus(Completed);
        var afterUpdate = DateTime.UtcNow;

        // Assert
        Assert.Equal("Executing", originalDto.Status);
        Assert.Null(originalDto.CompletedAt);
        Assert.Equal(Completed, updatedDto.Status);
        Assert.NotNull(updatedDto.CompletedAt);
        Assert.True(beforeUpdate <= updatedDto.CompletedAt);
        Assert.True(updatedDto.CompletedAt <= afterUpdate);
        Assert.NotNull(updatedDto.StartedAt);
    }

    [Fact]
    public void ShouldSetCompletedAt_WhenUsingWithStatusUsingFailed()
    {
        // Arrange
        var originalDto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Executing",
            Verbosity = "Normal",
            StartedAt = DateTime.UtcNow.AddMinutes(-3)
        };

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var updatedDto = originalDto.WithStatus(Failed);
        var afterUpdate = DateTime.UtcNow;

        // Assert
        Assert.Equal("Executing", originalDto.Status);
        Assert.Null(originalDto.CompletedAt);
        Assert.Equal(Failed, updatedDto.Status);
        Assert.NotNull(updatedDto.CompletedAt);
        Assert.True(beforeUpdate <= updatedDto.CompletedAt);
        Assert.True(updatedDto.CompletedAt <= afterUpdate);
    }

    [Fact]
    public void ShouldOnlyUpdateStatus_WhenUsingWithStatusWithOtherStatus()
    {
        // Arrange
        var originalDto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        // Act
        var updatedDto = originalDto.WithStatus("Paused");

        // Assert
        Assert.Equal("Ready", originalDto.Status);
        Assert.Equal("Paused", updatedDto.Status);
        Assert.Null(updatedDto.StartedAt);
        Assert.Null(updatedDto.CompletedAt);
    }

    [Fact]
    public void ShouldBeSettable_WhenAllowingCodeExecution()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            AllowCodeExecution = true
        };

        // Assert
        Assert.True(dto.AllowCodeExecution);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingMaxAgents()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            MaxAgents = 20
        };

        // Assert
        Assert.Equal(20, dto.MaxAgents);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingMaxTasks()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            MaxTasks = 200
        };

        // Assert
        Assert.Equal(200, dto.MaxTasks);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingExecutionTimeout()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            ExecutionTimeout = TimeSpan.FromHours(2)
        };

        // Assert
        Assert.Equal(TimeSpan.FromHours(2), dto.ExecutionTimeout);
    }

    [Fact]
    public void ShouldBeOptional_WhenUsingMemoryConfiguration()
    {
        // Arrange
        var memoryConfig = new MemoryConfigurationDto
        {
            Provider = "Redis",
            EnableShortTerm = true,
            EnableLongTerm = true,
            EnableEpisodic = false,
            MaxShortTermEntries = 500,
            MaxLongTermEntries = 5000
        };

        // Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            MemoryConfiguration = memoryConfig
        };

        // Assert
        Assert.NotNull(dto.MemoryConfiguration);
        Assert.Equal("Redis", dto.MemoryConfiguration.Provider);
        Assert.True(dto.MemoryConfiguration.EnableShortTerm);
        Assert.True(dto.MemoryConfiguration.EnableLongTerm);
        Assert.False(dto.MemoryConfiguration.EnableEpisodic);
    }

    [Fact]
    public void ShouldBeOptional_WhenUsingPerformanceMetrics()
    {
        // Arrange
        var metrics = new PerformanceMetricsDto
        {
            SuccessCount = 47,
            FailureCount = 3,
            AverageResponseTime = 300,
            Throughput = 50.0,
            ErrorRate = 5.0
        };

        // Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            PerformanceMetrics = metrics
        };

        // Assert
        Assert.NotNull(dto.PerformanceMetrics);
        Assert.Equal(47, dto.PerformanceMetrics.SuccessCount);
        Assert.Equal(3, dto.PerformanceMetrics.FailureCount);
        Assert.Equal(300, dto.PerformanceMetrics.AverageResponseTime);
    }

    [Fact]
    public void ShouldAcceptImmutableList_WhenUsingExecutionHistory()
    {
        // Arrange
        var history = ImmutableList<ExecutionHistoryDto>.Empty
            .Add(new ExecutionHistoryDto
            {
                Id = "exec-1",
                CrewId = CrewId1,
                Status = Completed,
                StartedAt = DateTime.UtcNow.AddHours(-2),
                CompletedAt = DateTime.UtcNow.AddHours(-1),
                TasksCompleted = 10,
                TasksFailed = 0,
                SuccessRate = 1.0
            })
            .Add(new ExecutionHistoryDto
            {
                Id = "exec-2",
                CrewId = CrewId1,
                Status = Failed,
                StartedAt = DateTime.UtcNow.AddDays(-1),
                CompletedAt = DateTime.UtcNow.AddDays(-1).AddHours(1),
                TasksCompleted = 8,
                TasksFailed = 2,
                SuccessRate = 0.8
            });

        // Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            ExecutionHistory = history
        };

        // Assert
        Assert.Equal(2, dto.ExecutionHistory.Count);
        Assert.Equal("exec-1", dto.ExecutionHistory[0].Id);
        Assert.Equal("exec-2", dto.ExecutionHistory[1].Id);
    }

    [Fact]
    public void ShouldAcceptImmutableDictionary_WhenUsingMetadata()
    {
        // Arrange
        var metadata = ImmutableDictionary<string, object>.Empty
            .Add("environment", "production")
            .Add("version", "2.0")
            .Add("priority", 1)
            .Add("tags", s_criticalAutomated);

        // Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal",
            Metadata = metadata
        };

        // Assert
        Assert.Equal(4, dto.Metadata.Count);
        Assert.Equal("production", dto.Metadata["environment"]);
        Assert.Equal("2.0", dto.Metadata["version"]);
        Assert.Equal(1, dto.Metadata["priority"]);
        Assert.IsType<string[]>(dto.Metadata["tags"]);
    }

    [Fact]
    public void ShouldBeSetAutomatically_WhenUsingCreatedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(beforeCreation <= dto.CreatedAt);
        Assert.True(dto.CreatedAt <= afterCreation);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var id = "crew-123";
        var name = "Test Crew";
        var description = "A test crew";
        var processType = "Sequential";
        var status = "Ready";
        var verbosity = "Normal";
        var createdAt = DateTime.UtcNow; // Use same timestamp for both DTOs

        // Act
        var dto1 = new CrewDto
        {
            Id = id,
            Name = name,
            Description = description,
            ProcessType = processType,
            Status = status,
            Verbosity = verbosity,
            CreatedAt = createdAt
        };

        var dto2 = new CrewDto
        {
            Id = id,
            Name = name,
            Description = description,
            ProcessType = processType,
            Status = status,
            Verbosity = verbosity,
            CreatedAt = createdAt
        };

        // Assert
        Assert.Equal(dto1, dto2);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var dto1 = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew 1",
            Description = "First crew",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        var dto2 = new CrewDto
        {
            Id = CrewId2,
            Name = "Crew 2",
            Description = "Second crew",
            ProcessType = "Parallel",
            Status = "Executing",
            Verbosity = "Debug"
        };

        // Assert
        Assert.NotEqual(dto1, dto2);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteCrewWithAllProperties()
    {
        // Arrange
        var agents = ImmutableList<AgentDto>.Empty.Add(new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready"
        });

        var tasks = ImmutableList<TaskDto>.Empty.Add(new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Task description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = "High"
        });

        var memoryConfig = new MemoryConfigurationDto
        {
            Provider = "Redis",
            EnableShortTerm = true
        };

        var performanceMetrics = new PerformanceMetricsDto
        {
            SuccessCount = 98,
            FailureCount = 2,
            Throughput = 100.0
        };

        var executionHistory = ImmutableList<ExecutionHistoryDto>.Empty.Add(new ExecutionHistoryDto
        {
            Id = "exec-1",
            CrewId = "crew-complete",
            Status = Completed,
            TasksCompleted = 5,
            TasksFailed = 0,
            SuccessRate = 1.0
        });

        var metadata = ImmutableDictionary<string, object>.Empty.Add("key", "value");

        // Act
        var dto = new CrewDto
        {
            Id = "crew-complete",
            Name = "Complete Crew",
            Description = "A complete crew with all properties",
            ProcessType = "Hierarchical",
            Status = Active,
            Verbosity = "Debug",
            AllowCodeExecution = true,
            MaxAgents = 15,
            MaxTasks = 150,
            ExecutionTimeout = TimeSpan.FromHours(1),
            Agents = agents,
            Tasks = tasks,
            MemoryConfiguration = memoryConfig,
            PerformanceMetrics = performanceMetrics,
            ExecutionHistory = executionHistory,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            CompletedAt = DateTime.UtcNow,
            Metadata = metadata
        };

        // Assert all properties
        Assert.Equal("crew-complete", dto.Id);
        Assert.Equal("Complete Crew", dto.Name);
        Assert.Equal("A complete crew with all properties", dto.Description);
        Assert.Equal("Hierarchical", dto.ProcessType);
        Assert.Equal(Active, dto.Status);
        Assert.Equal("Debug", dto.Verbosity);
        Assert.True(dto.AllowCodeExecution);
        Assert.Equal(15, dto.MaxAgents);
        Assert.Equal(150, dto.MaxTasks);
        Assert.Equal(TimeSpan.FromHours(1), dto.ExecutionTimeout);
        Assert.Single(dto.Agents);
        Assert.Single(dto.Tasks);
        Assert.NotNull(dto.MemoryConfiguration);
        Assert.NotNull(dto.PerformanceMetrics);
        Assert.Single(dto.ExecutionHistory);
        Assert.NotNull(dto.StartedAt);
        Assert.NotNull(dto.CompletedAt);
        Assert.Single(dto.Metadata);
        Assert.Equal(1, dto.AgentCount);
        Assert.Equal(1, dto.TaskCount);
    }

    [Fact]
    public void ShouldUpdateCorrectly_WhenUsingWithStatusWithMultipleCalls()
    {
        // Arrange
        var originalDto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        // Act
        var dto1 = originalDto.WithStatus("Executing");
        var dto2 = dto1.WithStatus("Paused");
        var dto3 = dto2.WithStatus("Executing");
        var dto4 = dto3.WithStatus(Completed);

        // Assert
        Assert.Equal("Ready", originalDto.Status);
        Assert.Null(originalDto.StartedAt);
        Assert.Null(originalDto.CompletedAt);

        Assert.Equal("Executing", dto1.Status);
        Assert.NotNull(dto1.StartedAt);
        Assert.Null(dto1.CompletedAt);

        Assert.Equal("Paused", dto2.Status);
        Assert.NotNull(dto2.StartedAt);
        Assert.Null(dto2.CompletedAt);

        Assert.Equal("Executing", dto3.Status);
        Assert.NotNull(dto3.StartedAt);
        Assert.Null(dto3.CompletedAt);

        Assert.Equal(Completed, dto4.Status);
        Assert.NotNull(dto4.StartedAt);
        Assert.NotNull(dto4.CompletedAt);
    }

    [Fact]
    public void ShouldHaveZeroCounts_WhenUsingEmptyCollections()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        // Assert
        Assert.Equal(0, dto.AgentCount);
        Assert.Equal(0, dto.TaskCount);
        Assert.True(dto.Agents.IsEmpty);
        Assert.True(dto.Tasks.IsEmpty);
        Assert.True(dto.ExecutionHistory.IsEmpty);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithStatus()
    {
        // Arrange
        var originalDto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        // Act
        var updatedDto = originalDto.WithStatus("Executing");

        // Assert
        Assert.NotSame(originalDto, updatedDto);
        Assert.Equal("Ready", originalDto.Status);
        Assert.Equal("Executing", updatedDto.Status);
    }
}
