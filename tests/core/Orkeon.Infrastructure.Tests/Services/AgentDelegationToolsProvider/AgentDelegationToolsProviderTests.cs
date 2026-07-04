using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Agent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Infrastructure.Tests.Services;

public class AgentDelegationToolsProviderTests
{
    private readonly TestAgentCommunicationService _communicationService;
    private readonly TestAgentExecutionService _agentExecutionService;
    private readonly DelegationTestLogger<AgentDelegationToolsProvider> _logger;
    private readonly AgentDelegationToolsProvider _provider;

    public AgentDelegationToolsProviderTests()
    {
        _communicationService = new TestAgentCommunicationService();
        _agentExecutionService = new TestAgentExecutionService();
        _logger = new DelegationTestLogger<AgentDelegationToolsProvider>();
        _provider = new AgentDelegationToolsProvider(_communicationService, _agentExecutionService, _logger);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullCommunicationService()
    {
        // Arrange
        IAgentCommunicationService? communicationService = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new AgentDelegationToolsProvider(communicationService!, _agentExecutionService, _logger));
        Assert.Equal("communicationService", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullLogger()
    {
        // Arrange
        ILogger<AgentDelegationToolsProvider>? logger = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new AgentDelegationToolsProvider(_communicationService, _agentExecutionService, logger!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructorWithValidParameters()
    {
        // Arrange & Act
        var provider = new AgentDelegationToolsProvider(_communicationService, _agentExecutionService, _logger);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenRegisterAgentWithNullAgentId()
    {
        // Arrange
        AgentId? agentId = null;
        var role = RoleDeveloper;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => _provider.RegisterAgent(agentId!, role));
        Assert.Equal("agentId", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenRegisterAgentWithNullRole()
    {
        // Arrange
        var agentId = AgentId.Create();
        string? role = null;

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => _provider.RegisterAgent(agentId, role!));
        Assert.Equal("role", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenRegisterAgentWithEmptyRole()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = "";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _provider.RegisterAgent(agentId, role));
        Assert.Equal("role", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenRegisterAgentWithWhitespaceRole()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = "   ";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _provider.RegisterAgent(agentId, role));
        Assert.Equal("role", exception.ParamName);
    }

    [Fact]
    public void ShouldRegisterAgent_WhenRegisterAgentWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = RoleDeveloper;

        // Act
        _provider.RegisterAgent(agentId, role);

        // Assert
        Assert.True(_logger.HasLoggedDebug($"Registered agent {agentId} with role {role}"));
    }

    [Fact]
    public void ShouldOverwritePreviousRegistration_WhenRegisterAgentWithDifferentCaseRoles()
    {
        // Arrange
        var agentId1 = AgentId.Create();
        var agentId2 = AgentId.Create();

        // Act
        _provider.RegisterAgent(agentId1, RoleDeveloper);
        _provider.RegisterAgent(agentId2, "DEVELOPER");

        // Assert - Second registration should overwrite first
        Assert.Equal(2, _logger.LogMessages.Count(m => m.Contains("Registered agent")));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenAddDelegationToolsToAgentWithNullAgent()
    {
        // Arrange
        DomainAgent? agent = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => _provider.AddDelegationToolsToAgent(agent!));
        Assert.Equal("agent", exception.ParamName);
    }

    [Fact]
    public void ShouldNotAddTools_WhenAddDelegationToolsToAgentWithAgentNotAllowingDelegation()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .Build();
        var initialToolCount = agent.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert
        Assert.Equal(initialToolCount, agent.Tools.Count);
        Assert.True(_logger.HasLoggedDebug($"Agent {agent.Id} does not allow delegation"));
    }

    [Fact]
    public void ShouldAddTools_WhenAddDelegationToolsToAgentWithAgentAllowingDelegation()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .Build();
        var initialToolCount = agent.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert
        Assert.Equal(initialToolCount + 2, agent.Tools.Count);
        Assert.Contains(agent.Tools, t => t.GetType().Name == "DelegateWorkTool");
        Assert.Contains(agent.Tools, t => t.GetType().Name == "AskQuestionTool");
        Assert.True(_logger.HasLoggedInformation($"Added delegation tools to agent {agent.Id}"));
    }

    [Fact]
    public void ShouldThrowException_WhenAddDelegationToolsToAgentMultipleTimes()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .Build();

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert - Second call should throw because tools already exist
        var exception = Assert.Throws<InvalidOperationException>(
            () => _provider.AddDelegationToolsToAgent(agent));
        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenRegisterAndDelegateCompleteWorkflow()
    {
        // Arrange
        var sender = new AgentBuilder()
            .Role(RoleManager)
            .Goal("Manage team")
            .AllowDelegation()
            .Build();

        var receiver = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .Build();

        var senderInitialToolCount = sender.Tools.Count;
        var receiverInitialToolCount = receiver.Tools.Count;

        // Act
        _provider.RegisterAgent(receiver.Id, RoleDeveloper);
        _provider.RegisterAgent(sender.Id, RoleManager);
        _provider.AddDelegationToolsToAgent(sender);
        _provider.AddDelegationToolsToAgent(receiver);

        // Assert
        Assert.Equal(senderInitialToolCount + 2, sender.Tools.Count);
        Assert.Equal(receiverInitialToolCount, receiver.Tools.Count); // No tools added because delegation is false
    }

    [Fact]
    public void ShouldKeepLastRegistration_WhenRegisterAgentMultipleAgentsWithSameRole()
    {
        // Arrange
        var agent1 = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .Build();

        var agent2 = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .Build();

        var agent3 = new AgentBuilder()
            .Role(RoleManager)
            .Goal("Manage team")
            .AllowDelegation()
            .Build();

        var agent3InitialToolCount = agent3.Tools.Count;

        // Act
        _provider.RegisterAgent(agent1.Id, RoleDeveloper);
        _provider.RegisterAgent(agent2.Id, RoleDeveloper); // Should overwrite agent1
        _provider.RegisterAgent(agent3.Id, RoleManager);

        _provider.AddDelegationToolsToAgent(agent3);

        // Assert
        Assert.Equal(3, _logger.LogMessages.Count(m => m.Contains("Registered agent")));
        Assert.Equal(agent3InitialToolCount + 2, agent3.Tools.Count);
    }

    [Fact]
    public void ShouldRegisterCorrectly_WhenRegisterAgentWithSpecialCharactersInRole()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = "Senior Developer (Full-Stack)";

        // Act
        _provider.RegisterAgent(agentId, role);

        // Assert
        Assert.True(_logger.HasLoggedDebug($"Registered agent {agentId} with role {role}"));
    }

    [Fact]
    public void ShouldAddToolsToEach_WhenAddDelegationToolsToAgentWithDifferentAgentRoles()
    {
        // Arrange
        var agent1 = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .Build();

        var agent2 = new AgentBuilder()
            .Role("Tester")
            .Goal("Test software")
            .AllowDelegation()
            .Build();

        var agent3 = new AgentBuilder()
            .Role(RoleManager)
            .Goal("Manage team")
            .AllowDelegation()
            .Build();

        var agent1InitialCount = agent1.Tools.Count;
        var agent2InitialCount = agent2.Tools.Count;
        var agent3InitialCount = agent3.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent1);
        _provider.AddDelegationToolsToAgent(agent2);
        _provider.AddDelegationToolsToAgent(agent3);

        // Assert
        Assert.Equal(agent1InitialCount + 2, agent1.Tools.Count);
        Assert.Equal(agent2InitialCount + 2, agent2.Tools.Count);
        Assert.Equal(agent3InitialCount + 2, agent3.Tools.Count);
        Assert.Equal(3, _logger.LogMessages.Count(m => m.Contains("Added delegation tools")));
    }

    [Fact]
    public void ShouldTreatAsSame_WhenRegisterAgentCaseInsensitiveRoles()
    {
        // Arrange
        var agentId1 = AgentId.Create();
        var agentId2 = AgentId.Create();

        // Act
        _provider.RegisterAgent(agentId1, "developer");
        _provider.RegisterAgent(agentId2, "DEVELOPER");

        // Assert
        // Second registration should overwrite the first one
        Assert.Equal(2, _logger.LogMessages.Count(m => m.Contains("Registered agent")));
        // Both should log with their original case
        Assert.Contains(_logger.LogMessages, m => m.Contains("role developer"));
        Assert.Contains(_logger.LogMessages, m => m.Contains("role DEVELOPER"));
    }

    [Fact]
    public void ShouldRegisterCorrectly_WhenRegisterAgentWithVeryLongRole()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = new string('a', 1000);

        // Act
        _provider.RegisterAgent(agentId, role);

        // Assert
        Assert.True(_logger.HasLoggedDebug($"Registered agent {agentId} with role {role}"));
    }

    [Fact]
    public void ShouldRegisterCorrectly_WhenRegisterAgentWithUnicodeRole()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = "Développeur Sénior 👨‍💻";

        // Act
        _provider.RegisterAgent(agentId, role);

        // Assert
        Assert.True(_logger.HasLoggedDebug($"Registered agent {agentId} with role {role}"));
    }

    [Fact]
    public void ShouldUpdateRole_WhenRegisterAgentOverwriteSameAgent()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        _provider.RegisterAgent(agentId, RoleDeveloper);
        _provider.RegisterAgent(agentId, RoleSeniorDeveloper);
        _provider.RegisterAgent(agentId, "Lead Developer");

        // Assert
        Assert.Equal(3, _logger.LogMessages.Count(m => m.Contains("Registered agent")));
        Assert.True(_logger.HasLoggedDebug($"Registered agent {agentId} with role Lead Developer"));
    }

    [Fact]
    public void ShouldAddDelegationTools_WhenAddDelegationToolsToAgentWithAgentHavingExistingTools()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .Build();

        // Add some existing tools first
        agent.AddTool(new SimpleTool("ExistingTool1"));
        agent.AddTool(new SimpleTool("ExistingTool2"));
        var initialToolCount = agent.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert
        Assert.Equal(initialToolCount + 2, agent.Tools.Count);
        Assert.Contains(agent.Tools, t => t.GetType().Name == "DelegateWorkTool");
        Assert.Contains(agent.Tools, t => t.GetType().Name == "AskQuestionTool");
    }

    [Fact]
    public void ShouldRespectLimit_WhenAddDelegationToolsToAgentWithMaxIterationsSet()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .MaxIterations(5)
            .Build();

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert
        Assert.Equal(2, agent.Tools.Count);
        Assert.Equal(5, agent.MaxIterations);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenRegisterAgentManyAgents()
    {
        // Arrange
        var agents = new List<(AgentId id, string role)>();
        for (int i = 0; i < 100; i++)
        {
            agents.Add((AgentId.Create(), $"Role{i}"));
        }

        // Act
        foreach (var (id, role) in agents)
        {
            _provider.RegisterAgent(id, role);
        }

        // Assert
        Assert.Equal(100, _logger.LogMessages.Count(m => m.Contains("Registered agent")));
    }

    [Fact]
    public void ShouldStillAddTools_WhenAddDelegationToolsToAgentWithBackstorySet()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .Backstory("Experienced developer with 10 years in the field")
            .AllowDelegation()
            .Build();
        var initialToolCount = agent.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert
        Assert.Equal(initialToolCount + 2, agent.Tools.Count);
        Assert.NotNull(agent.Backstory);
    }

    [Fact]
    public void ShouldAddTools_WhenAddDelegationToolsToAgentWithVerboseMode()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .AllowDelegation()
            .Verbose()
            .Build();
        var initialToolCount = agent.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert
        Assert.Equal(initialToolCount + 2, agent.Tools.Count);
        Assert.True(agent.Verbose);
    }

    [Fact]
    public void ShouldRegisterCorrectly_WhenRegisterAgentWithNumericRole()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = "Agent007";

        // Act
        _provider.RegisterAgent(agentId, role);

        // Assert
        Assert.True(_logger.HasLoggedDebug($"Registered agent {agentId} with role {role}"));
    }

    [Fact]
    public void ShouldAddTools_WhenAddDelegationToolsToAgentWithSystemTemplateSet()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop software")
            .SystemTemplate("You are a helpful assistant")
            .AllowDelegation()
            .Build();
        var initialToolCount = agent.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert
        Assert.Equal(initialToolCount + 2, agent.Tools.Count);
        Assert.NotEmpty(agent.SystemTemplate!);
    }

    // ── FEAT-07: Tests for RegisterAgentEntity, UpdateExecutionContext, sync delegation ──

    [Fact]
    public void ShouldRegisterAgentEntityAndRole_WhenRegisterAgentEntity()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Analyst")
            .Goal("Analyze")
            .Build();

        // Act
        _provider.RegisterAgentEntity(agent);

        // Assert — RegisterAgentEntity calls RegisterAgent internally, which logs
        Assert.True(_logger.HasLoggedDebug($"Registered agent {agent.Id} with role Analyst"));
    }

    [Fact]
    public void ShouldUpdateExecutionContext_WhenUpdateExecutionContextCalled()
    {
        // Arrange
        using var memoryScope = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.Create(),
            new Dictionary<string, string> { ["key"] = "value" },
            memoryScope,
            [],
            CancellationToken.None);

        // Act & Assert — should not throw
        var exception = Record.Exception(() => _provider.UpdateExecutionContext(context));
        Assert.Null(exception);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldInjectExecutionService_WhenAddDelegationToolsToAgentWithSyncDelegation()
    {
        // Arrange — register a worker agent entity
        var workerAgent = new AgentBuilder()
            .Role("Worker")
            .Goal("Do work")
            .Build();
        _provider.RegisterAgentEntity(workerAgent);

        // Configure the execution service to return a success result
        var toolIdentity = new ToolCallIdentity("tool-1", "file_read", "agent-1", "task-1");
        var toolUsage = ToolUsage.CreateSuccess(toolIdentity, TimeSpan.FromMilliseconds(100));
        _agentExecutionService.ResultToReturn = new Application.Interfaces.Services.TaskResult(
            true, "Worker result", null,
            [toolUsage], TimeSpan.FromSeconds(2), null, 100);

        // Set up execution context
        using var memoryScope = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.Create(),
            new Dictionary<string, string>(),
            memoryScope,
            [],
            CancellationToken.None);
        _provider.UpdateExecutionContext(context);

        // Create a supervisor that allows delegation
        var supervisor = new AgentBuilder()
            .Role("Supervisor")
            .Goal("Supervise")
            .AllowDelegation()
            .Build();
        _provider.RegisterAgentEntity(supervisor);
        _provider.AddDelegationToolsToAgent(supervisor);

        // Find the DelegateWorkTool
        var delegateTool = supervisor.Tools.First(t => t.GetType().Name == "DelegateWorkTool");

        // Act — call with wait_for_result=true
        var request = new Orkeon.Domain.Tools.Protocol.ToolCallRequest(
            ToolName: "delegate_work_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Do analysis",
                ["context"] = "Some context",
                ["coworker_role"] = "Worker",
                ["wait_for_result"] = true
            });
        var response = await delegateTool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success, $"Expected success but got error: {response.Error}");
        Assert.NotNull(response.Result);
        var resultDict = Assert.IsType<Dictionary<string, object?>>(response.Result);
        Assert.True(resultDict.ContainsKey("result"), "Result dictionary should contain 'result' key");
        Assert.Contains("Worker result", resultDict["result"]?.ToString() ?? "");
    }

    [Fact]
    public void ShouldNotAddDelegationTools_WhenAgentHasNoDelegationFlag()
    {
        // Arrange — agent with AllowDelegation = false (default)
        var agent = new AgentBuilder()
            .Role("Analyst")
            .Goal("Analyze data")
            .Build();
        var initialToolCount = agent.Tools.Count;

        // Act
        _provider.AddDelegationToolsToAgent(agent);

        // Assert — no tools should be added
        Assert.Equal(initialToolCount, agent.Tools.Count);
        Assert.True(_logger.HasLoggedDebug($"Agent {agent.Id} does not allow delegation"));
    }
}

// Test doubles
internal class TestAgentCommunicationService : IAgentCommunicationService
{
    private readonly List<AgentMessage> _sentMessages = [];
    private readonly HashSet<AgentId> _availableAgents = [];

    public IReadOnlyList<AgentMessage> SentMessages => _sentMessages;

    public ValueTask SendMessageAsync(AgentMessage message)
    {
        _sentMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId)
    {
        var messages = _sentMessages.Where(m => m.To.Equals(agentId));
        foreach (var message in messages)
        {
            await Task.Yield(); // Ensure async operation
            yield return message;
        }
    }

    public Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_availableAgents.Contains(agentId));
    }

    public void SetAgentAvailable(AgentId agentId)
    {
        _availableAgents.Add(agentId);
    }
}

// Stub agent execution service for testing — supports configurable ResultToReturn
internal class TestAgentExecutionService : IAgentExecutionService
{
    public TaskResult? ResultToReturn { get; set; }

    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(ResultToReturn ?? new TaskResult(true, "stub", null, [], TimeSpan.Zero));

    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default) where TOutput : class
        => System.Threading.Tasks.Task.FromResult(new TaskResult<TOutput>(true, "stub", null, [], TimeSpan.Zero));

    public System.Threading.Tasks.Task<TaskExecutionPlan> PlanTaskExecutionAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new TaskExecutionPlan(agent.Id, [], TimeSpan.Zero, 1.0));

    public System.Threading.Tasks.Task<bool> CanExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(true);
}

// Test memory scope for creating SimpleExecutionContext in tests
internal sealed class TestMemoryScope : Orkeon.Application.Interfaces.Ports.IMemoryScope
{
    public string AgentId => "test-agent";
    public string ScopeId => "test-scope";
    public System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation) => operation();
    public System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation) => operation();
    public void Dispose() { }
}

// Simple tool for testing
internal class SimpleTool : Orkeon.Domain.Common.ITool
{
    public string Name { get; }
    public string Description => $"{Name} description";
    public ToolSchema Schema { get; }

    public SimpleTool(string name)
    {
        Name = name;
        Schema = new ToolSchema(Name, Description, []);
    }

    public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolCallResponse(
            Success: true,
            Result: Success,
            Error: null));
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ToolResult.CreateSuccess(Success));
    }

    public bool ValidateInput(string input)
    {
        return true;
    }
}

// Test logger with unique name to avoid conflicts
internal class DelegationTestLogger<T> : ILogger<T>
{
    private readonly List<string> _logMessages = [];

    public IReadOnlyList<string> LogMessages => _logMessages;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _logMessages.Add($"[{logLevel}] {message}");
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool HasLoggedWarning(string message) =>
        _logMessages.Any(m => m.Contains("[Warning]") && m.Contains(message));

    public bool HasLoggedError(string message) =>
        _logMessages.Any(m => m.Contains("[Error]") && m.Contains(message));

    public bool HasLoggedInformation(string message) =>
        _logMessages.Any(m => m.Contains("[Information]") && m.Contains(message));

    public bool HasLoggedDebug(string message) =>
        _logMessages.Any(m => m.Contains("[Debug]") && m.Contains(message));
}

