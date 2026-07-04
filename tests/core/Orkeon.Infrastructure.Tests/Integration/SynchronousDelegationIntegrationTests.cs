using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Infrastructure.Tests.Integration;

/// <summary>
/// Integration tests validating the full wiring from
/// AgentDelegationToolsProvider -> DelegateWorkTool -> IAgentExecutionService
/// for the synchronous delegation (wait_for_result=true) supervisor pattern.
/// </summary>
public sealed class SynchronousDelegationIntegrationTests
{
    [Fact]
    public async Task Supervisor_DelegatesWithSyncResult_ReturnsWorkerOutput()
    {
        // Arrange
        // 1. Create test doubles (reuse existing mocks from Doubles folder where possible)
        var commService = new TrackingCommunicationService();
        var execService = new MockAgentExecutionService();
        var logger = new NullLogger<AgentDelegationToolsProvider>();

        // 2. Create provider (same as DI would)
        var provider = new AgentDelegationToolsProvider(commService, execService, logger);

        // 3. Create agents
        var worker = new AgentBuilder()
            .Role("Data Analyst")
            .Goal("Analyze datasets")
            .Build();

        var supervisor = new AgentBuilder()
            .Role("Supervisor")
            .Goal("Coordinate analysis tasks")
            .AllowDelegation()
            .Build();

        // 4. Register agents (as SequentialProcessStrategy would)
        provider.RegisterAgentEntity(worker);
        provider.RegisterAgentEntity(supervisor);

        // 5. Set up execution context (as strategy would)
        using var memoryScope = new MockMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.Create(),
            new Dictionary<string, string> { ["project"] = "test-project" },
            memoryScope,
            [],
            CancellationToken.None);
        provider.UpdateExecutionContext(context);

        // 6. Add delegation tools (as strategy would)
        provider.AddDelegationToolsToAgent(supervisor);

        // 7. Configure mock execution service to return a successful result
        var toolIdentity = new ToolCallIdentity("t1", "file_read", "agent1", "task1");
        var toolIdentity2 = new ToolCallIdentity("t2", "csv_parser", "agent1", "task1");
        execService.SetExecuteResult(new TaskResult(
            Success: true,
            Output: "Analysis complete: 42 records processed, 3 anomalies found",
            StructuredOutput: null,
            ToolsUsed:
            [
                ToolUsage.CreateSuccess(toolIdentity, TimeSpan.FromSeconds(1)),
                ToolUsage.CreateSuccess(toolIdentity2, TimeSpan.FromSeconds(2))
            ],
            ExecutionTime: TimeSpan.FromSeconds(5),
            Error: null,
            TokensUsed: 500));

        // 8. Find the DelegateWorkTool added to the supervisor
        var delegateTool = supervisor.Tools.First(t => t.GetType().Name == "DelegateWorkTool");

        // Act -- simulate supervisor calling delegate_work with wait_for_result=true
        var request = new ToolCallRequest(
            ToolName: "delegate_work",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Analyze the Q4 sales dataset",
                ["context"] = "Focus on anomalies and outliers",
                ["coworker_role"] = "Data Analyst",
                ["expected_output"] = "Summary with anomaly count",
                ["wait_for_result"] = true
            });
        var response = await delegateTool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert -- the tool call itself succeeded
        Assert.True(response.Success, $"Expected success but got error: {response.Error}");

        // The result should be a dictionary with delegation details
        var result = response.Result as Dictionary<string, object?>;
        Assert.NotNull(result);
        Assert.Equal(true, result["success"]);
        Assert.Contains("42 records processed", result["result"]?.ToString() ?? "");
        Assert.Equal("Analyze the Q4 sales dataset", result["task_description"]?.ToString());

        // Verify execution service was called with correct agent (worker, not supervisor)
        Assert.NotNull(execService.LastExecuteAgent);
        Assert.Equal("Data Analyst", execService.LastExecuteAgent!.Role.Value);

        // Verify execution service was called with a task
        Assert.NotNull(execService.LastExecuteTask);

        // Verify context propagation: delegation_context and parent variables
        Assert.NotNull(execService.LastExecuteContext);
        Assert.Equal("Focus on anomalies and outliers", execService.LastExecuteContext!.Variables["delegation_context"]);
        Assert.Equal("test-project", execService.LastExecuteContext.Variables["project"]);

        // Verify no async message was sent (sync mode, not fire-and-forget)
        Assert.Empty(commService.SentMessages);
    }

    [Fact]
    public async Task Supervisor_DelegatesWithSyncResult_PropagatesFailure()
    {
        // Arrange -- same setup pattern as test 1
        var commService = new TrackingCommunicationService();
        var execService = new MockAgentExecutionService();
        var logger = new NullLogger<AgentDelegationToolsProvider>();
        var provider = new AgentDelegationToolsProvider(commService, execService, logger);

        var worker = new AgentBuilder().Role("Explorer").Goal("Explore files").Build();
        var supervisor = new AgentBuilder().Role("Supervisor").Goal("Coordinate").AllowDelegation().Build();

        provider.RegisterAgentEntity(worker);
        provider.RegisterAgentEntity(supervisor);
        using var memoryScope = new MockMemoryScope();
        provider.UpdateExecutionContext(new SimpleExecutionContext(
            CrewId.Create(),
            new Dictionary<string, string>(),
            memoryScope,
            [],
            CancellationToken.None));
        provider.AddDelegationToolsToAgent(supervisor);

        // Configure mock to return failure
        execService.SetExecuteResult(new TaskResult(
            Success: false,
            Output: "",
            StructuredOutput: null,
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.FromMilliseconds(200),
            Error: "Directory /src not found",
            TokensUsed: 50));

        var delegateTool = supervisor.Tools.First(t => t.GetType().Name == "DelegateWorkTool");

        // Act
        var request = new ToolCallRequest(
            ToolName: "delegate_work",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "List all TypeScript files in /src",
                ["context"] = "",
                ["coworker_role"] = "Explorer",
                ["wait_for_result"] = true
            });
        var response = await delegateTool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert -- the typed pipeline propagates DelegateWorkResponse.Success=false
        // as ToolCallResponse.Success=false with the error message
        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Contains("not found", response.Error);

        // The result dictionary contains the full delegation failure details
        var result = response.Result as Dictionary<string, object?>;
        Assert.NotNull(result);
        Assert.Equal(false, result["success"]);
        Assert.Contains("not found", result["error"]?.ToString() ?? "");
        Assert.Contains("failed", result["message"]?.ToString() ?? "");
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────

/// <summary>
/// Communication service that tracks sent messages for assertion.
/// Extends the basic MockAgentCommunicationService with message capture.
/// </summary>
internal sealed class TrackingCommunicationService : IAgentCommunicationService
{
    public List<AgentMessage> SentMessages { get; } = [];

    public ValueTask SendMessageAsync(AgentMessage message)
    {
        SentMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken ct = default)
        => Task.FromResult(true);
}
