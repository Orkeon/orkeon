using System.Collections.Immutable;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.DTOs.Agents;

public class AgentDtoTests
{
    private static readonly string[] s_skills = ["C#", "Python", "JavaScript"];
    private static readonly string[] s_languages = ["English", "French"];
    private static readonly string[] s_tools = ["Tool1", "Tool2"];
    private static readonly string[] s_productionCritical = ["production", "critical"];

    [Fact]
    public void ShouldCreateValidDto_WhenConstructingWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new AgentDto
        {
            Id = "agent-123",
            Name = "Test Agent",
            Role = RoleDeveloper,
            Goal = "Write quality code",
            Backstory = "Experienced developer",
            Type = "WorkerAgent",
            Status = Active
        };

        // Assert
        Assert.Equal("agent-123", dto.Id);
        Assert.Equal("Test Agent", dto.Name);
        Assert.Equal(RoleDeveloper, dto.Role);
        Assert.Equal("Write quality code", dto.Goal);
        Assert.Equal("Experienced developer", dto.Backstory);
        Assert.Equal("WorkerAgent", dto.Type);
        Assert.Equal(Active, dto.Status);
    }

    [Fact]
    public void ShouldBeSetCorrectly_WhenUsingDefaultValues()
    {
        // Arrange & Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready"
        };

        // Assert
        Assert.False(dto.Verbose);
        Assert.False(dto.AllowDelegation);
        Assert.Equal(300, dto.MaxExecutionTime);
        Assert.True(dto.Tools.IsEmpty);
        Assert.Null(dto.Llm);
        Assert.Null(dto.Capabilities);
        Assert.Null(dto.PerformanceMetrics);
        Assert.Null(dto.UpdatedAt);
        Assert.True(dto.Metadata.IsEmpty);
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedStatus_WhenUsingWithStatus()
    {
        // Arrange
        var originalDto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready"
        };

        // Act
        var updatedDto = originalDto.WithStatus("Busy");

        // Assert
        Assert.Equal("Ready", originalDto.Status);
        Assert.Null(originalDto.UpdatedAt);
        Assert.Equal("Busy", updatedDto.Status);
        Assert.NotNull(updatedDto.UpdatedAt);
        Assert.True(updatedDto.UpdatedAt <= DateTime.UtcNow);
        Assert.NotSame(originalDto, updatedDto);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedMetrics_WhenUsingWithPerformanceMetrics()
    {
        // Arrange
        var originalDto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready"
        };

        var metrics = new PerformanceMetricsDto
        {
            SuccessCount = 9,
            FailureCount = 1,
            AverageResponseTime = 150,
            Throughput = 10.0,
            ErrorRate = 10.0
        };

        // Act
        var updatedDto = originalDto.WithPerformanceMetrics(metrics);

        // Assert
        Assert.Null(originalDto.PerformanceMetrics);
        Assert.Null(originalDto.UpdatedAt);
        Assert.Equal(metrics, updatedDto.PerformanceMetrics);
        Assert.NotNull(updatedDto.UpdatedAt);
        Assert.True(updatedDto.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldAcceptImmutableList_WhenUsingTools()
    {
        // Arrange
        var tools = ImmutableList<string>.Empty
            .Add("FileReadTool")
            .Add("WebScrapeTool")
            .Add(ToolSearch);

        // Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            Tools = tools
        };

        // Assert
        Assert.Equal(3, dto.Tools.Count);
        Assert.Contains("FileReadTool", dto.Tools);
        Assert.Contains("WebScrapeTool", dto.Tools);
        Assert.Contains(ToolSearch, dto.Tools);
    }

    [Fact]
    public void ShouldAcceptImmutableDictionary_WhenUsingMetadata()
    {
        // Arrange
        var metadata = ImmutableDictionary<string, object>.Empty
            .Add("version", "1.0")
            .Add("priority", 5)
            .Add("tags", s_productionCritical);

        // Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            Metadata = metadata
        };

        // Assert
        Assert.Equal(3, dto.Metadata.Count);
        Assert.Equal("1.0", dto.Metadata["version"]);
        Assert.Equal(5, dto.Metadata["priority"]);
        Assert.IsType<string[]>(dto.Metadata["tags"]);
    }

    [Fact]
    public void ShouldBeOptional_WhenUsingLlm()
    {
        // Arrange
        var llmDto = new LlmDto
        {
            Provider = "OpenAI",
            Model = ModelGpt4,
            Temperature = 0.7,
            MaxTokens = 2000
        };

        // Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            Llm = llmDto
        };

        // Assert
        Assert.NotNull(dto.Llm);
        Assert.Equal("OpenAI", dto.Llm.Provider);
        Assert.Equal(ModelGpt4, dto.Llm.Model);
    }

    [Fact]
    public void ShouldBeOptional_WhenUsingCapabilities()
    {
        // Arrange
        var capabilities = new AgentCapabilitiesDto
        {
            Skills = ImmutableList<string>.Empty.AddRange(s_skills),
            Languages = ImmutableList<string>.Empty.AddRange(s_languages),
            OverallConfidence = "High"
        };

        // Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            Capabilities = capabilities
        };

        // Assert
        Assert.NotNull(dto.Capabilities);
        Assert.Equal(3, dto.Capabilities.Skills.Count);
        Assert.Equal(2, dto.Capabilities.Languages.Count);
        Assert.Equal("High", dto.Capabilities.OverallConfidence);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingVerbose()
    {
        // Arrange & Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            Verbose = true
        };

        // Assert
        Assert.True(dto.Verbose);
    }

    [Fact]
    public void ShouldBeSettable_WhenAllowingDelegation()
    {
        // Arrange & Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            AllowDelegation = true
        };

        // Assert
        Assert.True(dto.AllowDelegation);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingMaxExecutionTime()
    {
        // Arrange & Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            MaxExecutionTime = 600
        };

        // Assert
        Assert.Equal(600, dto.MaxExecutionTime);
    }

    [Fact]
    public void ShouldBeSetAutomatically_WhenUsingCreatedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready"
        };
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(beforeCreation <= dto.CreatedAt);
        Assert.True(dto.CreatedAt <= afterCreation);
    }

    [Fact]
    public void ShouldBeNullInitially_WhenUsingUpdatedAt()
    {
        // Arrange & Act
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready"
        };

        // Assert
        Assert.Null(dto.UpdatedAt);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var id = "agent-123";
        var name = "Test Agent";
        var role = RoleDeveloper;
        var goal = GoalWriteCode;
        var backstory = "Experienced";
        var type = RoleWorker;
        var status = Active;
        var createdAt = DateTime.UtcNow; // Use same timestamp for both DTOs

        // Act
        var dto1 = new AgentDto
        {
            Id = id,
            Name = name,
            Role = role,
            Goal = goal,
            Backstory = backstory,
            Type = type,
            Status = status,
            CreatedAt = createdAt
        };

        var dto2 = new AgentDto
        {
            Id = id,
            Name = name,
            Role = role,
            Goal = goal,
            Backstory = backstory,
            Type = type,
            Status = status,
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
        var dto1 = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent1",
            Role = RoleDeveloper,
            Goal = GoalWriteCode,
            Backstory = "Experienced",
            Type = RoleWorker,
            Status = Active
        };

        var dto2 = new AgentDto
        {
            Id = AgentId2,
            Name = "Agent2",
            Role = "Tester",
            Goal = "Test code",
            Backstory = "QA Expert",
            Type = RoleWorker,
            Status = "Ready"
        };

        // Assert
        Assert.NotEqual(dto1, dto2);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteAgentWithAllProperties()
    {
        // Arrange
        var tools = ImmutableList<string>.Empty.AddRange(s_tools);
        var metadata = ImmutableDictionary<string, object>.Empty.Add("key", "value");
        var llm = new LlmDto { Provider = "OpenAI", Model = ModelGpt4, Temperature = 0.7, MaxTokens = 1000 };
        var capabilities = new AgentCapabilitiesDto { OverallConfidence = "High" };
        var performanceMetrics = new PerformanceMetricsDto { SuccessCount = 4, FailureCount = 1 };

        // Act
        var dto = new AgentDto
        {
            Id = "agent-complete",
            Name = "Complete Agent",
            Role = "Multi-role",
            Goal = "Handle complex tasks",
            Backstory = "Versatile agent with multiple skills",
            Type = "Advanced",
            Status = Active,
            Verbose = true,
            AllowDelegation = true,
            MaxExecutionTime = 1200,
            Tools = tools,
            Llm = llm,
            Capabilities = capabilities,
            PerformanceMetrics = performanceMetrics,
            UpdatedAt = DateTime.UtcNow,
            Metadata = metadata
        };

        // Assert all properties
        Assert.Equal("agent-complete", dto.Id);
        Assert.Equal("Complete Agent", dto.Name);
        Assert.Equal("Multi-role", dto.Role);
        Assert.Equal("Handle complex tasks", dto.Goal);
        Assert.Equal("Versatile agent with multiple skills", dto.Backstory);
        Assert.Equal("Advanced", dto.Type);
        Assert.Equal(Active, dto.Status);
        Assert.True(dto.Verbose);
        Assert.True(dto.AllowDelegation);
        Assert.Equal(1200, dto.MaxExecutionTime);
        Assert.Equal(2, dto.Tools.Count);
        Assert.NotNull(dto.Llm);
        Assert.NotNull(dto.Capabilities);
        Assert.NotNull(dto.PerformanceMetrics);
        Assert.NotNull(dto.UpdatedAt);
        Assert.Single(dto.Metadata);
    }

    [Fact]
    public void ShouldUpdateCorrectly_WhenUsingWithStatusWithMultipleCalls()
    {
        // Arrange
        var originalDto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready"
        };

        // Act
        var dto1 = originalDto.WithStatus("Busy");
        var dto2 = dto1.WithStatus("Idle");
        var dto3 = dto2.WithStatus("Terminated");

        // Assert
        Assert.Equal("Ready", originalDto.Status);
        Assert.Equal("Busy", dto1.Status);
        Assert.Equal("Idle", dto2.Status);
        Assert.Equal("Terminated", dto3.Status);
        Assert.NotNull(dto1.UpdatedAt);
        Assert.NotNull(dto2.UpdatedAt);
        Assert.NotNull(dto3.UpdatedAt);
        Assert.True(dto1.UpdatedAt <= dto2.UpdatedAt);
        Assert.True(dto2.UpdatedAt <= dto3.UpdatedAt);
    }

    [Fact]
    public void ShouldSetToNull_WhenUsingWithPerformanceMetricsWithNullMetrics()
    {
        // Arrange
        var dto = new AgentDto
        {
            Id = AgentId1,
            Name = "Agent",
            Role = RoleWorker,
            Goal = GoalCompleteTasks,
            Backstory = "Worker agent",
            Type = RoleWorker,
            Status = "Ready",
            PerformanceMetrics = new PerformanceMetricsDto { SuccessCount = 5, FailureCount = 0 }
        };

        // Act
        var updatedDto = dto.WithPerformanceMetrics(null!);

        // Assert
        Assert.NotNull(dto.PerformanceMetrics);
        Assert.Null(updatedDto.PerformanceMetrics);
        Assert.NotNull(updatedDto.UpdatedAt);
    }
}
