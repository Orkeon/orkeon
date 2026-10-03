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
        Assert.Null(dto.Llm);
        Assert.Null(dto.Capabilities);
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
    public void ShouldMapAllAgents_WhenUsingToDtoUsingEnumerableAgents()
    {
        // Arrange
        var agents = new List<DomainAgent>
        {
            DomainAgent.Create(AgentRole.From(RoleDeveloper), AgentGoal.From(GoalWriteCode)),
            DomainAgent.Create(AgentRole.From("Designer"), AgentGoal.From("Create designs")),
            DomainAgent.Create(AgentRole.From(RoleManager), AgentGoal.From("Lead team"))
        };

        // Act
        var dtos = AgentMapper.ToDto(agents);

        // Assert
        Assert.Equal(3, dtos.Count);
        Assert.Contains(dtos, d => d.Role == RoleDeveloper);
        Assert.Contains(dtos, d => d.Role == "Designer");
        Assert.Contains(dtos, d => d.Role == RoleManager);
    }

    [Fact]
    public void ShouldCreateSimplifiedDto_WhenUsingToSummaryDto()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From(RoleAnalyst),
            AgentGoal.From(GoalAnalyzeData),
            AgentBackstory.From("Data expert"),
            allowDelegation: false,
            verbose: true);

        agent.AddTool(new TestTool("AnalysisTool", "Analysis tool"));

        // Act
        var dto = AgentMapper.ToSummaryDto(agent);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(agent.Id, dto.Id);
        Assert.Equal(RoleAnalyst, dto.Role);
        Assert.Equal(GoalAnalyzeData, dto.Goal);
        Assert.Equal(agent.Status.ToString(), dto.Status);
        Assert.True(dto.Verbose);
        Assert.False(dto.AllowDelegation);

        // Summary DTO now includes backstory (required property on sealed record)
        Assert.Equal("Data expert", dto.Backstory);
        Assert.Empty(dto.Tools);
        Assert.Null(dto.PerformanceMetrics);
        Assert.Null(dto.Capabilities);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToDtoWithNullAgent()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => AgentMapper.ToDto((DomainAgent)null!));
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenUsingToDtoUsingEnumerableAgentsWithEmptyList()
    {
        // Arrange
        var agents = new List<DomainAgent>();

        // Act
        var dtos = AgentMapper.ToDto(agents);

        // Assert
        Assert.NotNull(dtos);
        Assert.Empty(dtos);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToDtoUsingEnumerableAgentsWithNullEnumerable()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => AgentMapper.ToDto((IEnumerable<DomainAgent>)null!));
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
    public void ShouldNotIncludeDetailedInformation_WhenUsingToSummaryDto()
    {
        // Arrange
        var agent = DomainAgent.Create(
            AgentRole.From("Security Expert"),
            AgentGoal.From("Ensure security"),
            AgentBackstory.From("Former security consultant with extensive experience"));

        agent.AddTool(new TestTool("SecurityScanner", "Scans for vulnerabilities"));
        agent.AddTool(new TestTool("PenTestTool", "Penetration testing tool"));

        // Act
        var dto = AgentMapper.ToSummaryDto(agent);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(agent.Id, dto.Id);
        Assert.Equal("Security Expert", dto.Role);
        Assert.Equal("Ensure security", dto.Goal);
        Assert.Equal(agent.Status.ToString(), dto.Status);

        // Backstory is now included (required property on sealed record)
        Assert.Equal("Former security consultant with extensive experience", dto.Backstory);
        Assert.Empty(dto.Tools);
        Assert.Null(dto.PerformanceMetrics);
        Assert.Null(dto.Capabilities);
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
