using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Agent.DynamicAgent;

/// <summary>
/// Tests for AgentSpawnRequest value object.
/// </summary>
public class AgentSpawnRequestTests
{
    private readonly CrewId _crewId = CrewId.Create();
    private readonly AgentRole _role = AgentRole.From("Researcher");
    private readonly AgentGoal _goal = AgentGoal.From("Find information");

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var request = new AgentSpawnRequest(
            _role,
            _goal,
            _crewId,
            AgentBackstory.From("An expert researcher"),
            true,
            5,
            200,
            true);

        // Assert
        Assert.NotNull(request);
        Assert.Equal(_role, request.Role);
        Assert.Equal(_goal, request.Goal);
        Assert.Equal(_crewId, request.ParentCrewId);
        Assert.NotNull(request.Backstory);
        Assert.True(request.AllowDelegation);
        Assert.Equal(5, request.MaxIterations);
        Assert.Equal(200, request.MaxRpm);
        Assert.True(request.Verbose);
    }

    [Fact]
    public void Constructor_WithNullRole_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AgentSpawnRequest(null!, _goal, _crewId));
    }

    [Fact]
    public void Constructor_WithNullGoal_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AgentSpawnRequest(_role, null!, _crewId));
    }

    [Fact]
    public void Constructor_WithNullParentCrewId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AgentSpawnRequest(_role, _goal, null!));
    }

    [Fact]
    public void Constructor_WithInvalidMaxIterations_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new AgentSpawnRequest(_role, _goal, _crewId, maxIterations: 0));
        Assert.Contains("maxIterations must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_WithInvalidMaxRpm_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new AgentSpawnRequest(_role, _goal, _crewId, maxRpm: -1));
        Assert.Contains("maxRpm must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_WithTools_StoresTools()
    {
        // Arrange
        var tools = new List<ITool> { new FakeTool("TestTool") };

        // Act
        var request = new AgentSpawnRequest(_role, _goal, _crewId, tools: tools);

        // Assert
        Assert.Single(request.Tools);
        Assert.Equal("TestTool", request.Tools[0].Name);
    }

    private sealed class FakeTool : ITool
    {
        public FakeTool(string name)
        {
            Name = name;
            Schema = new ToolSchema(name, "fake tool", new Dictionary<string, ParameterSchema>());
        }

        public string Name { get; }
        public string Description => "fake tool";
        public ToolSchema Schema { get; }

        public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, null, null));

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess(string.Empty));

        public bool ValidateInput(string input) => true;
    }

    [Fact]
    public void Constructor_WithMetadata_StoresMetadata()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            { "specialization", "ML" },
            { "priority", 1 }
        }.AsReadOnly();

        // Act
        var request = new AgentSpawnRequest(_role, _goal, _crewId, metadata: metadata);

        // Assert
        Assert.Equal(2, request.Metadata.Count);
        Assert.Equal("ML", request.Metadata["specialization"]);
        Assert.Equal(1, request.Metadata["priority"]);
    }

    [Fact]
    public void CreateBuilder_ReturnsBuilder()
    {
        // Act
        var builder = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId);

        // Assert
        Assert.NotNull(builder);
    }
}

/// <summary>
/// Tests for AgentSpawnRequestBuilder fluent API.
/// </summary>
public class AgentSpawnRequestBuilderTests
{
    private readonly CrewId _crewId = CrewId.Create();
    private readonly AgentRole _role = AgentRole.From(RoleAnalyst);
    private readonly AgentGoal _goal = AgentGoal.From(GoalAnalyzeData);

    [Fact]
    public void Builder_BuildsRequestWithDefaults()
    {
        // Arrange
        var builder = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId);

        // Act
        var request = builder.Build();

        // Assert
        Assert.NotNull(request);
        Assert.Equal(_role, request.Role);
        Assert.Equal(_goal, request.Goal);
        Assert.False(request.AllowDelegation);
        Assert.Equal(AgentDefaults.MaxIterations, request.MaxIterations);
    }

    [Fact]
    public void Builder_WithBackstory_SetsBackstory()
    {
        // Arrange
        var backstory = AgentBackstory.From("Expert analyst");
        var builder = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId)
            .WithBackstory(backstory);

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal(backstory, request.Backstory);
    }

    [Fact]
    public void Builder_AllowDelegation_EnablesDelegation()
    {
        // Arrange
        var builder = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId)
            .AllowDelegation(true);

        // Act
        var request = builder.Build();

        // Assert
        Assert.True(request.AllowDelegation);
    }

    [Fact]
    public void Builder_WithTool_AddsTool()
    {
        // Arrange
        var builder = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId)
            .WithTool(new FakeTool("WebSearch"));

        // Act
        var request = builder.Build();

        // Assert
        Assert.Single(request.Tools);
        Assert.Equal("WebSearch", request.Tools[0].Name);
    }

    private sealed class FakeTool : ITool
    {
        public FakeTool(string name)
        {
            Name = name;
            Schema = new ToolSchema(name, "fake tool", new Dictionary<string, ParameterSchema>());
        }

        public string Name { get; }
        public string Description => "fake tool";
        public ToolSchema Schema { get; }

        public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, null, null));

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess(string.Empty));

        public bool ValidateInput(string input) => true;
    }

    [Fact]
    public void Builder_WithMetadata_AddsMetadata()
    {
        // Arrange
        var builder = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId)
            .WithMetadata("domain", "finance")
            .WithMetadata("priority", "high");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal(2, request.Metadata.Count);
        Assert.Equal("finance", request.Metadata["domain"]);
        Assert.Equal("high", request.Metadata["priority"]);
    }

    [Fact]
    public void Builder_Chaining_AllowsFluentConfiguration()
    {
        // Arrange & Act
        var request = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId)
            .AllowDelegation(true)
            .Verbose(true)
            .MaxIterations(10)
            .MaxRpm(300)
            .Build();

        // Assert
        Assert.True(request.AllowDelegation);
        Assert.True(request.Verbose);
        Assert.Equal(10, request.MaxIterations);
        Assert.Equal(300, request.MaxRpm);
    }
}
