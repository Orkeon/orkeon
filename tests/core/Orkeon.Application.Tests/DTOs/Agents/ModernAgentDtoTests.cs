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
        Assert.Null(dto.MaxExecutionTime);
        Assert.True(dto.Tools.IsEmpty);
        Assert.Null(dto.UpdatedAt);
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
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

}
