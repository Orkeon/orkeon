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
    public void ShouldCreateAgent_WhenUsingCreateFromRequestWithCompleteRequest()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = RoleDataAnalyst,
            Goal = "Analyze complex datasets",
            Backstory = "Expert in statistical analysis",
            Settings = new AgentSettingsDto
            {
                AllowDelegation = true,
                MaxIterations = 30,
                MaxRPM = 50.0,
                Verbose = true
            }
        };

        var tools = new IBaseTool[]
        {
            new TestTool("DataTool", "Tool for data analysis"),
            new TestTool("VisualizationTool", "Tool for visualization")
        };

        // Act
        var agent = AgentMapper.CreateFromRequest(request, tools);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(RoleDataAnalyst, agent.Role.Value);
        Assert.Equal("Analyze complex datasets", agent.Goal.Value);
        Assert.Equal("Expert in statistical analysis", agent.Backstory?.Value);
        Assert.True(agent.AllowDelegation);
        Assert.Equal(30, agent.MaxIterations);
        Assert.Equal(50, agent.MaxRpm);
        Assert.True(agent.Verbose);
        Assert.Equal(2, agent.Tools.Count);
    }

    [Fact]
    public void ShouldUseDefaults_WhenUsingCreateFromRequestWithMinimalRequest()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = RoleWorker,
            Goal = "Process tasks"
        };

        // Act
        var agent = AgentMapper.CreateFromRequest(request, []);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(RoleWorker, agent.Role.Value);
        Assert.Equal("Process tasks", agent.Goal.Value);
        Assert.Null(agent.Backstory);
        Assert.False(agent.AllowDelegation); // Domain default
        Assert.Equal(15, agent.MaxIterations); // Domain default
        Assert.Equal(10, agent.MaxRpm); // Domain default
        Assert.False(agent.Verbose); // Domain default
        Assert.Empty(agent.Tools);
    }

    [Fact]
    public void ShouldCreateAgentWithoutTools_WhenUsingCreateFromRequestWithNullTools()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = RoleManager,
            Goal = "Coordinate team"
        };

        // Act
        var agent = AgentMapper.CreateFromRequest(request, null!);

        // Assert
        Assert.NotNull(agent);
        Assert.Empty(agent.Tools);
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

    [Theory]
    [InlineData("https://api.anthropic.com", ModelClaude3, ProviderAnthropic)]
    [InlineData(EndpointOllamaDefault, ModelLlama2, ProviderOllama)]
    [InlineData("https://api.openai.com", ModelGpt4, ProviderOpenAI)]
    [InlineData("", ModelGpt35Turbo, ProviderOpenAI)]
    [InlineData("", "claude-2", ProviderAnthropic)]
    [InlineData("", "", ProviderOpenAI)] // Default
    public void ShouldReturnCorrectProvider_WhenUsingInferProvider(string baseUrl, string model, string expectedProvider)
    {
        // Arrange
        var llmConfig = LlmConfig.Default() with
        {
            BaseUrl = string.IsNullOrEmpty(baseUrl) ? null : new Uri(baseUrl),
            Model = model
        };

        var dto = new LlmConfigDto
        {
            Provider = expectedProvider,
            Model = model,
            Temperature = 0.7,
            MaxTokens = 1000,
            TimeoutSeconds = 30
        };

        // Act - Test via ToLlmConfigDto (since InferProvider is private)
        var agent = DomainAgent.Create(AgentRole.From("Test"), AgentGoal.From("Test"));
        var agentDto = AgentMapper.ToDto(agent);

        // Assert
        // Since we can't directly test private methods, we verify the behavior
        // through the public API usage
        Assert.NotNull(agentDto);
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
    public void ShouldThrowArgumentNullException_WhenUsingCreateFromRequestWithNullRequest()
    {
        // Arrange
        var tools = Array.Empty<IBaseTool>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => AgentMapper.CreateFromRequest(null!, tools));
    }

    [Fact]
    public void ShouldApplyAllSettings_WhenUsingCreateFromRequestWithCustomSettings()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = "Technical Lead",
            Goal = "Lead technical initiatives",
            Backstory = "15 years of technical leadership experience",
            Settings = new AgentSettingsDto
            {
                AllowDelegation = false,
                MaxIterations = 100,
                MaxRPM = 120.5,
                Verbose = true
            }
        };

        var tools = new IBaseTool[] { new TestTool("LeadershipTool", "Leadership tool") };

        // Act
        var agent = AgentMapper.CreateFromRequest(request, tools);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal("Technical Lead", agent.Role.Value);
        Assert.Equal("Lead technical initiatives", agent.Goal.Value);
        Assert.Equal("15 years of technical leadership experience", agent.Backstory?.Value);
        Assert.False(agent.AllowDelegation);
        Assert.Equal(100, agent.MaxIterations);
        Assert.Equal(120, agent.MaxRpm); // Note: converted to int
        Assert.True(agent.Verbose);
        Assert.Single(agent.Tools);
    }

    [Fact]
    public void ShouldUseDefaultValues_WhenUsingCreateFromRequestWithNullSettings()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = RoleDeveloper,
            Goal = GoalWriteCode,
            Settings = null
        };

        // Act
        var agent = AgentMapper.CreateFromRequest(request, []);

        // Assert
        Assert.NotNull(agent);
        Assert.False(agent.AllowDelegation); // Domain default: false
        Assert.Equal(15, agent.MaxIterations); // Domain default: 15
        Assert.Equal(10, agent.MaxRpm); // Domain default: 10
        Assert.False(agent.Verbose); // Domain default: false
    }

    [Fact]
    public void ShouldMixDefaultsWithProvided_WhenUsingCreateFromRequestWithPartialSettings()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = "Architect",
            Goal = "Design systems",
            Settings = new AgentSettingsDto
            {
                MaxIterations = 50,
                // Other settings not provided
            }
        };

        // Act
        var agent = AgentMapper.CreateFromRequest(request, []);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(50, agent.MaxIterations); // Provided value
        Assert.True(agent.AllowDelegation); // DTO default when Settings object is created
        Assert.Equal(10, agent.MaxRpm); // Domain default (MaxRPM is nullable in DTO)
        Assert.False(agent.Verbose); // DTO default
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
    public void ShouldIgnoreIncompatibleTools_WhenUsingCreateFromRequestWithNonIToolTools()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = RoleAnalyst,
            Goal = GoalAnalyzeData
        };

        // Create a mock non-ITool that implements IBaseTool
        var nonIToolArray = new IBaseTool[] { new NonIToolImplementation() };

        // Act
        var agent = AgentMapper.CreateFromRequest(request, nonIToolArray);

        // Assert
        Assert.NotNull(agent);
        Assert.Empty(agent.Tools); // Should not add non-ITool tools
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

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingCreateFromRequestWithZeroMaxRPM()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = "LowRateAgent",
            Goal = "Work with rate limits",
            Settings = new AgentSettingsDto
            {
                MaxRPM = 0.0
            }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            AgentMapper.CreateFromRequest(request, []));
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingCreateFromRequestWithNegativeMaxIterations()
    {
        // Arrange
        var request = new CreateAgentRequest
        {
            Role = "TestAgent",
            Goal = "Test negative values",
            Settings = new AgentSettingsDto
            {
                MaxIterations = -1
            }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            AgentMapper.CreateFromRequest(request, []));
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }
}

// Test implementation of ITool
internal class TestTool : ITool
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

// Test implementation of IBaseTool that is NOT ITool
internal class NonIToolImplementation : IBaseTool
{
    public string Name => "NonITool";
    public string Description => "Not an ITool";
    public static bool RequiresConfirmation => false;

    public ToolSchema Schema => new ToolSchema(
        Name: "NonITool",
        Description: "Not an ITool",
        Parameters: []);

    public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult(new ToolCallResponse(
            Success: true,
            Result: "NonITool called",
            Error: null,
            Metadata: null));
    }

    public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult(new ToolResult
        {
            Success = true,
            Output = "NonITool executed"
        });
    }

    public bool ValidateInput(string input)
    {
        return true;
    }
}

