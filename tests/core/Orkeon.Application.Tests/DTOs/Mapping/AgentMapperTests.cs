using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Common.Mapping;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.DTOs.Mapping;

public class AgentMapperTests
{
    [Fact]
    public void ShouldMapAllProperties_WhenUsingToDtoWithCompleteAgent()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From(RoleSeniorDeveloper),
            AgentGoal.From("Build high-quality software"),
            AgentBackstory.From("10 years of experience in software development"),
            allowDelegation: true,
            maxIterations: 25,
            maxRpm: 30,
            verbose: true);

        var testTool = new TestTool("TestTool", "Test tool description");
        agent.AddTool(testTool);

        // Act
        var dto = AgentMapper.ToDto(agent);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(agent.Id, dto.Id);
        Assert.Equal(RoleSeniorDeveloper, dto.Role);
        Assert.Equal("Build high-quality software", dto.Goal);
        Assert.Equal("10 years of experience in software development", dto.Backstory);
        Assert.True(dto.Verbose);
        Assert.True(dto.AllowDelegation);
        Assert.Single(dto.Tools);
        Assert.Contains("TestTool", dto.Tools);
        Assert.Equal(agent.Status.ToString(), dto.Status);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldReturnAgentSecondsOrNull_WhenAgentIsMapped()
    {
        var limited = new Orkeon.Domain.Agent.AgentBuilder().Role(RoleWorker).Goal(GoalCompleteTasks)
            .MaxExecutionTime(TimeSpan.FromSeconds(90)).Build();
        var unlimited = DomainAgent.Create(AgentRole.From(RoleWorker), AgentGoal.From(GoalCompleteTasks));

        Assert.Equal(90, AgentMapper.ToDto(limited).MaxExecutionTime);
        Assert.Null(AgentMapper.ToDto(unlimited).MaxExecutionTime);
    }

    public static TheoryData<double, int> RoundedLimits => new()
    {
        { 500, 1 },
        { 90500, 91 },
        { Orkeon.Domain.Constants.Agent.AgentDefaults.MaxExecutionTimeCeiling.TotalMilliseconds, 4294967 },
    };

    [Theory]
    [Trait("BR", "CH-02")]
    [MemberData(nameof(RoundedLimits))]
    public void ShouldRoundLimitUpToWholeSeconds_WhenAgentIsMapped(double milliseconds, int expectedSeconds)
    {
        var agent = new Orkeon.Domain.Agent.AgentBuilder().Role(RoleWorker).Goal(GoalCompleteTasks)
            .MaxExecutionTime(TimeSpan.FromMilliseconds(milliseconds)).Build();

        Assert.Equal(expectedSeconds, AgentMapper.ToDto(agent).MaxExecutionTime);
    }

    [Fact]
    public void ShouldMapWithDefaults_WhenUsingToDtoWithMinimalAgent()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From(RoleWorker),
            AgentGoal.From(GoalCompleteTasks));

        // Act
        var dto = AgentMapper.ToDto(agent);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(agent.Id, dto.Id);
        Assert.Equal(RoleWorker, dto.Role);
        Assert.Equal(GoalCompleteTasks, dto.Goal);
        Assert.Equal(string.Empty, dto.Backstory);
        Assert.False(dto.Verbose);
        Assert.False(dto.AllowDelegation); // Domain default is false
        Assert.Empty(dto.Tools);
    }

    [Fact]
    public void ShouldMapAllTools_WhenUsingToDtoWithMultipleTools()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From(RoleDeveloper),
            AgentGoal.From("Build software"));

        var tool1 = new TestTool("Tool1", "Description 1");
        var tool2 = new TestTool("Tool2", "Description 2");
        var tool3 = new TestTool("Tool3", "Description 3");

        agent.AddTool(tool1);
        agent.AddTool(tool2);
        agent.AddTool(tool3);

        // Act
        var dto = AgentMapper.ToDto(agent);

        // Assert
        Assert.Equal(3, dto.Tools.Count);
        Assert.Contains("Tool1", dto.Tools);
        Assert.Contains("Tool2", dto.Tools);
        Assert.Contains("Tool3", dto.Tools);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToDtoWithNullAgent()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => AgentMapper.ToDto((DomainAgent)null!));
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenUsingToDtoWithAgentContainingNullBackstory()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From("Tester"),
            AgentGoal.From("Test software"),
            backstory: null);

        // Act
        var dto = AgentMapper.ToDto(agent);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(string.Empty, dto.Backstory);
    }

    [Fact]
    public void ShouldMapTimestampsFromEntity_WhenUsingToDto()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From("DevOps"),
            AgentGoal.From("Maintain infrastructure"));

        // Act
        var dto = AgentMapper.ToDto(agent);

        // Assert — timestamps come from the entity, not fabricated at mapping time
        Assert.NotNull(dto);
        Assert.Equal(agent.CreatedAt, dto.CreatedAt);
        Assert.Equal(agent.UpdatedAt, dto.UpdatedAt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingToDtoWithMaxValues()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From("MaxAgent"),
            AgentGoal.From("Test maximum values"),
            AgentBackstory.From("Test backstory"),
            allowDelegation: true,
            maxIterations: int.MaxValue,
            maxRpm: int.MaxValue,
            verbose: true);

        // Act
        var dto = AgentMapper.ToDto(agent);

        // Assert
        Assert.NotNull(dto);
        Assert.True(dto.Verbose);
        Assert.True(dto.AllowDelegation);
    }
}

// Test implementation of IBaseTool
internal class TestTool : IBaseTool
{
    public TestTool(string name, string description)
    {
        Name = name;
        Description = description;
        Schema = new ToolSchema(
            Name: name,
            Description: description,
            Parameters: []);
    }

    public string Name { get; }
    public string Description { get; }
    public ToolSchema Schema { get; }

    public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult(new ToolCallResponse(
            Success: true,
            Result: $"Called {Name}",
            Error: null,
            Metadata: null));
    }

    public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult(new ToolResult
        {
            Success = true,
            Output = $"Executed {Name} with: {input}"
        });
    }

    public bool ValidateInput(string input)
    {
        return !string.IsNullOrEmpty(input);
    }

    public override string ToString() => Name;
}
