using Orkeon.Application.Context;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Infrastructure.Tools;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using AppTaskResult = Orkeon.Application.Interfaces.Services.TaskResult;
using AppTaskResultGeneric = Orkeon.Application.Interfaces.Services;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.Tools;

public sealed class DelegateWorkToolTests : IDisposable
{
    private readonly DelegateWorkTestAgentCommunicationService _communicationService;
    private readonly TestLogger<DelegateWorkTool> _logger;
    private readonly AgentId _currentAgentId;
    private readonly TestAgentFinder _agentFinder;
    private readonly DelegateWorkTool _tool;

    public DelegateWorkToolTests()
    {
        _communicationService = new DelegateWorkTestAgentCommunicationService();
        _logger = new TestLogger<DelegateWorkTool>();
        _currentAgentId = AgentId.Create();
        _agentFinder = new TestAgentFinder();
        _tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            _logger);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullCommunicationService()
    {
        // Arrange
        IAgentCommunicationService? communicationService = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new DelegateWorkTool(
                communicationService!,
                _currentAgentId,
                _agentFinder.FindAgentByRole,
                _logger));
        Assert.Equal("communicationService", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullCurrentAgentId()
    {
        // Arrange
        AgentId? currentAgentId = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new DelegateWorkTool(
                _communicationService,
                currentAgentId!,
                _agentFinder.FindAgentByRole,
                _logger));
        Assert.Equal("currentAgentId", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullFindAgentByRole()
    {
        // Arrange
        Func<string, AgentId?>? findAgentByRole = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new DelegateWorkTool(
                _communicationService,
                _currentAgentId,
                findAgentByRole!,
                _logger));
        Assert.Equal("findAgentByRole", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructorWithValidParameters()
    {
        // Arrange & Act
        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            _logger);

        // Assert
        Assert.NotNull(tool);
        Assert.Equal("delegate_work_to_coworker", tool.Name);
        Assert.Equal("Delegate a task to a coworker with specific expertise", tool.Description);
        Assert.Equal("Collaboration", tool.Category);
    }

    [Fact]
    public void ShouldReturnCorrectSchema_WhenSchema()
    {
        // Arrange & Act
        var schema = _tool.Schema;

        // Assert
        Assert.NotNull(schema);
        Assert.Equal("delegate_work_to_coworker", schema.Name);
        Assert.Equal(5, schema.Parameters.Count);
        Assert.True(schema.Parameters.ContainsKey("task"));
        Assert.True(schema.Parameters.ContainsKey("context"));
        Assert.True(schema.Parameters.ContainsKey("coworker_role"));
        Assert.True(schema.Parameters.ContainsKey("expected_output"));
        Assert.True(schema.Parameters.ContainsKey("wait_for_result"));
        Assert.True(schema.Parameters["task"].Required);
        Assert.True(schema.Parameters["coworker_role"].Required);
        Assert.False(schema.Parameters["expected_output"].Required);
        Assert.False(schema.Parameters["wait_for_result"].Required);
    }

    [Fact]
    public async Task ShouldDelegateSuccessfully_WhenExecuteCoreAsyncWithProtocolRequestValidParameters()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Implement user authentication",
                ["context"] = "Use JWT tokens",
                ["coworker_role"] = RoleDeveloper
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Contains("successfully delegated", resultDict["message"]!.ToString()!);
        Assert.Equal(targetAgentId.ToString(), resultDict["target_agent_id"]!.ToString());

        var sentMessage = (_communicationService.SentMessages.Count > 0 ? _communicationService.SentMessages[0] : null);
        Assert.NotNull(sentMessage);
        Assert.Equal(_currentAgentId, sentMessage.From);
        Assert.Equal(targetAgentId, sentMessage.To);
        Assert.Equal(MessageType.Task, sentMessage.Type);
        Assert.Equal("Implement user authentication", sentMessage.Content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithProtocolRequestMissingTaskParameter()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["context"] = "Some context",
                ["coworker_role"] = RoleDeveloper
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Result);
        Assert.Contains("Required parameter 'task' is missing", response.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithProtocolRequestMissingCoworkerRole()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Some task",
                ["context"] = "Some context"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Result);
        Assert.Contains("Required parameter 'coworker_role' is missing", response.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithProtocolRequestAgentNotFound()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Implement feature",
                ["context"] = "Details here",
                ["coworker_role"] = "NonExistentRole"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Result);
        Assert.Contains("Could not find agent with role: NonExistentRole", response.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithProtocolRequestCommunicationServiceThrows()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);
        _communicationService.ShouldThrowOnSend = true;

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Task that will fail",
                ["context"] = "Context",
                ["coworker_role"] = RoleDeveloper
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Result);
        Assert.Contains("Communication service error", response.Error);
    }

    [Fact]
    public async Task ShouldDelegateSuccessfully_WhenExecuteCoreAsyncWithProtocolRequestValidJson()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent("Tester", targetAgentId);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Write unit tests",
                ["context"] = "Use xUnit framework",
                ["coworker_role"] = "Tester"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Result);

        var sentMessage = (_communicationService.SentMessages.Count > 0 ? _communicationService.SentMessages[0] : null);
        Assert.NotNull(sentMessage);
        Assert.Equal("Write unit tests", sentMessage!.Content);
        Assert.Equal("Use xUnit framework", sentMessage.Metadata!["context"]);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithStringInputInvalidJson()
    {
        // Arrange
        var input = "{ invalid json }";

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Required parameter", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithStringInputNullParameters()
    {
        // Arrange
        var input = "null";

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Required parameter", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithStringInputAgentNotFound()
    {
        // Arrange - use snake_case keys to match schema
        var input = "{\"task\":\"Some task\",\"context\":\"Some context\",\"coworker_role\":\"UnknownRole\"}";

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Could not find agent with role:", result.Error);
    }

    [Fact]
    public void ShouldReturnTrue_WhenValidateInputWithValidJson()
    {
        // Arrange
        var input = JsonSerializer.Serialize(new
        {
            task = "Valid task",
            context = "Valid context",
            coworker_role = RoleDeveloper
        });

        // Act
        var result = _tool.ValidateInput(input);

        // Assert - typed tools validate via CallAsync, not ValidateInput
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldReturnError_WhenValidateInputWithInvalidJson()
    {
        // Arrange - typed tools validate through ExecuteAsync, not ValidateInput
        var input = "{ not valid json";

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Required parameter", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenValidateInputWithMissingTask()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["context"] = "Some context",
                ["coworker_role"] = RoleDeveloper
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("task", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenValidateInputWithEmptyTask()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "",
                ["context"] = "Some context",
                ["coworker_role"] = RoleDeveloper
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Task description is required", response.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenValidateInputWithMissingCoworkerRole()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Some task",
                ["context"] = "Some context"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("coworker_role", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenValidateInputWithEmptyCoworkerRole()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Some task",
                ["context"] = "Some context",
                ["coworker_role"] = ""
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Coworker role is required", response.Error);
    }

    [Fact]
    public async Task ShouldLogInformation_WhenExecuteCoreAsyncWithProtocolRequest()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleManager, targetAgentId);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Review code",
                ["context"] = "PR #123",
                ["coworker_role"] = RoleManager
            }
        );

        // Act
        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_logger.HasLoggedInformation($"Delegated task to {targetAgentId} (role: Manager)"));
    }

    [Fact]
    public async Task ShouldIncludeDelegatedMetadata_WhenExecuteCoreAsync()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent("Architect", targetAgentId);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Design system architecture",
                ["context"] = "Microservices approach",
                ["coworker_role"] = "Architect"
            }
        );

        // Act
        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var sentMessage = (_communicationService.SentMessages.Count > 0 ? _communicationService.SentMessages[0] : null);
        Assert.NotNull(sentMessage);
        Assert.True((bool)sentMessage!.Metadata!["delegated"]);
        Assert.Equal("Microservices approach", sentMessage.Metadata["context"]);
    }

    // ── Synchronous delegation tests ─────────────────────────────────────

    private static DomainAgent CreateTestAgent(string role = "Developer", string goal = "Write code")
        => new AgentBuilder().Role(role).Goal(goal).Build();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "NullMemoryScope (no-op Dispose) is owned by the returned execution context, which lives for the duration of the test.")]
    private static SimpleExecutionContext CreateTestContext(CancellationToken ct = default)
        => new(
            CrewId.Create(),
            new Dictionary<string, string>(),
            new NullMemoryScope(),
            Array.Empty<TaskOutput>(),
            ct);

    private static ToolUsage CreateToolUsage(string toolName)
        => new(
            new ToolCallIdentity(toolName, toolName, "agent-1", "task-1"),
            TimeSpan.FromMilliseconds(50),
            true);

    [Fact]
    public async Task ShouldSendMessage_WhenWaitForResultFalse()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);
        var executionService = new TestAgentExecutionService();

        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            executionService,
            _ => CreateTestAgent(),
            () => CreateTestContext(),
            _logger);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Implement feature X",
                ["context"] = "Use clean architecture",
                ["coworker_role"] = RoleDeveloper,
                ["wait_for_result"] = false
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success);
        Assert.True(_communicationService.SentMessages.Count > 0, "SendMessageAsync should have been called");
        Assert.Null(executionService.LastAgent); // ExecuteTaskAsync should NOT have been called
    }

    [Fact]
    public async Task ShouldExecuteTask_WhenWaitForResultTrue()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);
        var testAgent = CreateTestAgent(RoleDeveloper, GoalWriteCode);
        var executionService = new TestAgentExecutionService
        {
            ResultToReturn = new AppTaskResult(
                true, "Task completed successfully", null,
                new List<ToolUsage> { CreateToolUsage("CodeTool") },
                TimeSpan.FromSeconds(2))
        };

        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            executionService,
            _ => testAgent,
            () => CreateTestContext(),
            _logger);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Write unit tests",
                ["context"] = "For the auth module",
                ["coworker_role"] = RoleDeveloper,
                ["expected_output"] = "Test report",
                ["wait_for_result"] = true
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(executionService.LastAgent);
        Assert.Equal(testAgent, executionService.LastAgent);
        Assert.NotNull(executionService.LastTask);
    }

    [Fact]
    public async Task ShouldReturnSyncResult_WhenWaitForResultTrue()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);
        var executionService = new TestAgentExecutionService
        {
            ResultToReturn = new AppTaskResult(
                true, "Analysis complete: 42 issues found", null,
                new List<ToolUsage>
                {
                    CreateToolUsage("SearchTool"),
                    CreateToolUsage("FileReadTool")
                },
                TimeSpan.FromSeconds(1))
        };

        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            executionService,
            _ => CreateTestAgent(),
            () => CreateTestContext(),
            _logger);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Analyze codebase",
                ["context"] = "Focus on security",
                ["coworker_role"] = RoleDeveloper,
                ["wait_for_result"] = true
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success);
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Equal(true, resultDict["success"]);
        Assert.Contains("Analysis complete", resultDict["result"]?.ToString() ?? "");
        Assert.True(Convert.ToInt64(resultDict["execution_time_ms"]) >= 0);
        var toolsUsed = resultDict["tools_used"];
        Assert.NotNull(toolsUsed);
    }

    [Fact]
    public async Task ShouldPropagateFailure_WhenWaitForResultTrueAndAgentFails()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);
        var executionService = new TestAgentExecutionService
        {
            ResultToReturn = new AppTaskResult(
                false, "", null,
                Array.Empty<ToolUsage>(),
                TimeSpan.FromMilliseconds(100),
                "LLM rate limit exceeded")
        };

        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            executionService,
            _ => CreateTestAgent(),
            () => CreateTestContext(),
            _logger);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Process data",
                ["context"] = "Large dataset",
                ["coworker_role"] = RoleDeveloper,
                ["wait_for_result"] = true
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert — when the agent fails, the response Success propagates as false
        Assert.False(response.Success);
        Assert.Contains("LLM rate limit exceeded", response.Error ?? "");
    }

    [Fact]
    public async Task ShouldReturnError_WhenWaitForResultTrueAndNoExecutionService()
    {
        // Arrange — use the 4-param constructor (no IAgentExecutionService)
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);

        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            _logger);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Do something synchronously",
                ["context"] = "Context",
                ["coworker_role"] = RoleDeveloper,
                ["wait_for_result"] = true
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Synchronous delegation is not available", response.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenWaitForResultTrueAndAgentNotFound()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);
        var executionService = new TestAgentExecutionService
        {
            ResultToReturn = new AppTaskResult(true, "ok", null, Array.Empty<ToolUsage>(), TimeSpan.Zero)
        };

        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            executionService,
            _ => null, // findAgentById returns null
            () => CreateTestContext(),
            _logger);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Run analysis",
                ["context"] = "Context",
                ["coworker_role"] = RoleDeveloper,
                ["wait_for_result"] = true
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Could not load agent entity for id", response.Error);
    }

    [Fact]
    public async Task ShouldPropagateCancellation_WhenWaitForResultTrueAndCancelled()
    {
        // Arrange
        var targetAgentId = AgentId.Create();
        _agentFinder.RegisterAgent(RoleDeveloper, targetAgentId);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Pre-cancel

        var executionService = new TestAgentExecutionService
        {
            ShouldThrowCancellation = true
        };

        using var tool = new DelegateWorkTool(
            _communicationService,
            _currentAgentId,
            _agentFinder.FindAgentByRole,
            executionService,
            _ => CreateTestAgent(),
            () => CreateTestContext(cts.Token),
            _logger);

        var request = new ProtocolToolCallRequest(
            ToolName: "DelegateWorkTool",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Long running task",
                ["context"] = "Context",
                ["coworker_role"] = RoleDeveloper,
                ["wait_for_result"] = true
            }
        );

        // Act — CallAsync catches OperationCanceledException and returns a failure response
        var response = await tool.CallAsync(request, cts.Token);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("cancelled", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _tool.Dispose();
        GC.SuppressFinalize(this);
    }
}

// Test doubles  
internal class DelegateWorkTestAgentCommunicationService : IAgentCommunicationService
{
    private readonly List<AgentMessage> _sentMessages = [];

    public IReadOnlyList<AgentMessage> SentMessages => _sentMessages;
    public bool ShouldThrowOnSend { get; set; }

    public ValueTask SendMessageAsync(AgentMessage message)
    {
        if (ShouldThrowOnSend)
        {
            throw new InvalidOperationException("Communication service error");
        }

        _sentMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId)
    {
        await Task.Yield();
        foreach (var message in _sentMessages.Where(m => m.To.Equals(agentId)))
        {
            yield return message;
        }
    }

    public Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}

internal class TestAgentFinder
{
    private readonly Dictionary<string, AgentId> _agentsByRole = [];

    public void RegisterAgent(string role, AgentId agentId)
    {
        _agentsByRole[role] = agentId;
    }

    public AgentId? FindAgentByRole(string role)
    {
        return _agentsByRole.TryGetValue(role, out var agentId) ? agentId : null;
    }
}

internal class TestAgentExecutionService : IAgentExecutionService
{
    public AppTaskResult? ResultToReturn { get; set; }
    public bool ShouldThrowCancellation { get; set; }
    public DomainAgent? LastAgent { get; private set; }
    public ICrewTask? LastTask { get; private set; }

    public Task<AppTaskResult> ExecuteTaskAsync(DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowCancellation)
            throw new OperationCanceledException(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        LastAgent = agent;
        LastTask = task;
        return Task.FromResult(ResultToReturn ?? new AppTaskResult(false, "", null, [], TimeSpan.Zero, "No result configured"));
    }

    public Task<AppTaskResultGeneric.TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default) where TOutput : class
        => throw new NotImplementedException();

    public Task<TaskExecutionPlan> PlanTaskExecutionAsync(DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<bool> CanExecuteTaskAsync(DomainAgent agent, ICrewTask task, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

internal class TestLogger<T> : ILogger<T>
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

    public bool HasLoggedInformation(string message) =>
        _logMessages.Any(m => m.Contains("[Information]") && m.Contains(message));

    public bool HasLoggedError(string message) =>
        _logMessages.Any(m => m.Contains("[Error]") && m.Contains(message));
}
