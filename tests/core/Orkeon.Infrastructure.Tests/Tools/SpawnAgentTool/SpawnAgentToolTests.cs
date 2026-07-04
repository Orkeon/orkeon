using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using AppTaskResult = Orkeon.Application.Interfaces.Services.TaskResult;
using AppTaskResultGeneric = Orkeon.Application.Interfaces.Services;

namespace Orkeon.Infrastructure.Tests.Tools.SpawnAgentTool;

/// <summary>
/// Tests for <see cref="Orkeon.Infrastructure.Tools.SpawnAgentTool"/> focused
/// on the child-budget propagation fix (P2-SONAR-SMELL-13). These tests lock in
/// the behaviour that a spawned agent receives a reduced, derived
/// <see cref="AgentExecutionBudget"/> via the spawn request metadata and via
/// the execution context variables.
/// </summary>
public class SpawnAgentToolTests
{
    private readonly AgentId _parentAgentId = AgentId.Create();
    private readonly CrewId _parentCrewId = CrewId.Create();

    private static DomainAgent CreateTestAgent()
        => DomainAgent.Create(
            AgentRole.From("analyst"),
            AgentGoal.From("Analyze data"));

    private static AgentExecutionBudget CreateParentBudget()
    {
        var budget = new AgentExecutionBudget
        {
            MaxToolCalls = 10,
            MaxDelegationDepth = 3,
            MaxTokensConsumed = 10_000,
            MaxSpawnedAgents = 5,
            MaxWallTime = TimeSpan.FromMinutes(10)
        };

        // Pre-consume some of the parent's tool call budget so the child
        // derivation is non-trivial.
        for (var i = 0; i < 3; i++)
            budget.RecordToolCall();

        return budget;
    }

    private static CapturingAgentFactory CreateCapturingFactoryMock(out List<AgentSpawnRequest> capturedRequests)
    {
        var factory = new CapturingAgentFactory();
        capturedRequests = factory.Captured;
        return factory;
    }

    /// <summary>Hand-rolled <see cref="IAgentFactory"/> that records every spawn request.</summary>
    private sealed class CapturingAgentFactory : IAgentFactory
    {
        public List<AgentSpawnRequest> Captured { get; } = new();

        public DomainAgent CreateAgent(AgentSpawnRequest request)
        {
            Captured.Add(request);
            return DomainAgent.Create(request.Role, request.Goal, request.Backstory);
        }

        public Task<DomainAgent> CreateAgentAsync(AgentSpawnRequest request, CancellationToken cancellationToken = default)
        {
            Captured.Add(request);
            return Task.FromResult(DomainAgent.Create(request.Role, request.Goal, request.Backstory));
        }
    }

    [Fact]
    public async Task ShouldPropagateChildBudgetToSpawnRequestMetadata_WhenSpawningFireAndForget()
    {
        // Arrange
        var parentBudget = CreateParentBudget();
        var factoryMock = CreateCapturingFactoryMock(out var captured);

        using var tool = new Orkeon.Infrastructure.Tools.SpawnAgentTool(
            factoryMock,
            _parentAgentId,
            _parentCrewId,
            parentBudget);

        var request = new ProtocolToolCallRequest(
            ToolName: "spawn_agent",
            Parameters: new Dictionary<string, object?>
            {
                ["role"] = "summariser",
                ["goal"] = "Summarise the report",
                ["task"] = "Write a 5-line summary",
                ["wait_for_result"] = false
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success, response.Error);
        Assert.Single(captured);
        var spawnRequest = captured[0];

        // The spawn request must carry the derived child budget for downstream
        // components to honour.
        Assert.True(
            spawnRequest.Metadata.ContainsKey(Orkeon.Infrastructure.Tools.SpawnAgentTool.ChildBudgetMetadataKey),
            "Spawn request should carry the derived child budget in its metadata.");

        var attached = spawnRequest.Metadata[Orkeon.Infrastructure.Tools.SpawnAgentTool.ChildBudgetMetadataKey]
            as AgentExecutionBudget;
        Assert.NotNull(attached);

        // Delegation depth should be reduced by at least 1 relative to the parent.
        Assert.True(
            attached!.MaxDelegationDepth < parentBudget.MaxDelegationDepth,
            $"Child MaxDelegationDepth ({attached.MaxDelegationDepth}) should be strictly less than parent ({parentBudget.MaxDelegationDepth}).");

        // Tool calls should be reduced by the number already consumed by the parent.
        Assert.Equal(parentBudget.MaxToolCalls - parentBudget.CurrentToolCalls, attached.MaxToolCalls);

        // Spawned agents allowance should be reduced by 1 (the spawn we just recorded).
        Assert.Equal(parentBudget.MaxSpawnedAgents - parentBudget.CurrentSpawnedAgents, attached.MaxSpawnedAgents);
    }

    [Fact]
    public async Task ShouldExposeChildBudgetLimitsViaMetadata_WhenSpawningFireAndForget()
    {
        // Arrange
        var parentBudget = CreateParentBudget();
        var factoryMock = CreateCapturingFactoryMock(out var captured);

        using var tool = new Orkeon.Infrastructure.Tools.SpawnAgentTool(
            factoryMock,
            _parentAgentId,
            _parentCrewId,
            parentBudget);

        var request = new ProtocolToolCallRequest(
            ToolName: "spawn_agent",
            Parameters: new Dictionary<string, object?>
            {
                ["role"] = "critic",
                ["goal"] = "Critique the plan",
                ["task"] = "Identify 3 risks",
                ["wait_for_result"] = false
            }
        );

        // Act
        await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var spawnRequest = captured[0];
        Assert.Equal(parentBudget.MaxToolCalls - parentBudget.CurrentToolCalls, (int)spawnRequest.Metadata["child_budget_max_tool_calls"]);
        // MaxDelegationDepth - currentDepth - 1 (from CreateChildBudget semantics).
        Assert.Equal(Math.Max(0, parentBudget.MaxDelegationDepth - parentBudget.CurrentDelegationDepth - 1), (int)spawnRequest.Metadata["child_budget_max_delegation_depth"]);
        Assert.Equal(parentBudget.MaxTokensConsumed - parentBudget.CurrentTokensConsumed, (int)spawnRequest.Metadata["child_budget_max_tokens"]);
        // CurrentSpawnedAgents is now 1 because RecordSpawn() was called.
        Assert.Equal(parentBudget.MaxSpawnedAgents - parentBudget.CurrentSpawnedAgents, (int)spawnRequest.Metadata["child_budget_max_spawned_agents"]);
    }

    [Fact]
    public async Task ShouldPropagateChildBudgetLimitsToExecutionContextVariables_WhenWaitForResult()
    {
        // Arrange
        var parentBudget = CreateParentBudget();
        var factoryMock = CreateCapturingFactoryMock(out _);

        var execution = new CapturingExecutionService
        {
            ResultToReturn = new AppTaskResult(
                Success: true,
                Output: "done",
                StructuredOutput: null,
                ToolsUsed: [],
                ExecutionTime: TimeSpan.FromMilliseconds(10),
                TokensUsed: 42)
        };

        using var tool = new Orkeon.Infrastructure.Tools.SpawnAgentTool(
            factoryMock,
            _parentAgentId,
            _parentCrewId,
            parentBudget,
            executionService: execution);

        var request = new ProtocolToolCallRequest(
            ToolName: "spawn_agent",
            Parameters: new Dictionary<string, object?>
            {
                ["role"] = "researcher",
                ["goal"] = "Find the source",
                ["task"] = "Locate the primary reference",
                ["wait_for_result"] = true
            }
        );

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success, response.Error);
        Assert.NotNull(execution.LastContext);
        Assert.True(execution.LastContext!.Variables.ContainsKey(Orkeon.Infrastructure.Tools.SpawnAgentTool.ChildBudgetVariableKey));
        var limitsString = execution.LastContext.Variables[Orkeon.Infrastructure.Tools.SpawnAgentTool.ChildBudgetVariableKey];
        Assert.Contains("tool_calls=", limitsString);
        Assert.Contains("delegation_depth=", limitsString);
        Assert.Contains("tokens=", limitsString);
        Assert.Contains("spawned=", limitsString);
        Assert.Contains("wall_time=", limitsString);
    }

    [Fact]
    public async Task ShouldConsumeParentBudgetTokens_WhenWaitForResultAndTokensReported()
    {
        // Arrange
        var parentBudget = CreateParentBudget();
        var initialTokens = parentBudget.CurrentTokensConsumed;
        var factoryMock = CreateCapturingFactoryMock(out _);

        var execution = new CapturingExecutionService
        {
            ResultToReturn = new AppTaskResult(
                Success: true,
                Output: "ok",
                StructuredOutput: null,
                ToolsUsed: [],
                ExecutionTime: TimeSpan.FromMilliseconds(5),
                TokensUsed: 123)
        };

        using var tool = new Orkeon.Infrastructure.Tools.SpawnAgentTool(
            factoryMock,
            _parentAgentId,
            _parentCrewId,
            parentBudget,
            executionService: execution);

        var request = new ProtocolToolCallRequest(
            ToolName: "spawn_agent",
            Parameters: new Dictionary<string, object?>
            {
                ["role"] = "summariser",
                ["goal"] = "Summarise",
                ["task"] = "5 bullets",
                ["wait_for_result"] = true
            }
        );

        // Act
        await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert — the parent budget tracks tokens consumed by the spawned agent.
        Assert.Equal(initialTokens + 123, parentBudget.CurrentTokensConsumed);
    }

    [Fact]
    public async Task ShouldRecordSpawnAgainstParentBudget_WhenInvoked()
    {
        // Arrange
        var parentBudget = CreateParentBudget();
        var initialSpawns = parentBudget.CurrentSpawnedAgents;
        var factoryMock = CreateCapturingFactoryMock(out _);

        using var tool = new Orkeon.Infrastructure.Tools.SpawnAgentTool(
            factoryMock,
            _parentAgentId,
            _parentCrewId,
            parentBudget);

        var request = new ProtocolToolCallRequest(
            ToolName: "spawn_agent",
            Parameters: new Dictionary<string, object?>
            {
                ["role"] = "helper",
                ["goal"] = "Help",
                ["task"] = "Assist",
                ["wait_for_result"] = false
            }
        );

        // Act
        await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(initialSpawns + 1, parentBudget.CurrentSpawnedAgents);
    }

    /// <summary>Test double capturing the <see cref="SimpleExecutionContext"/> seen at execution time.</summary>
    private sealed class CapturingExecutionService : IAgentExecutionService
    {
        public AppTaskResult? ResultToReturn { get; set; }
        public SimpleExecutionContext? LastContext { get; private set; }
        public DomainAgent? LastAgent { get; private set; }
        public ICrewTask? LastTask { get; private set; }

        public Task<AppTaskResult> ExecuteTaskAsync(DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        {
            LastAgent = agent;
            LastTask = task;
            LastContext = context;
            return Task.FromResult(ResultToReturn
                ?? new AppTaskResult(false, "", null, [], TimeSpan.Zero, "No result configured"));
        }

        public Task<AppTaskResultGeneric.TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
            where TOutput : class
            => throw new NotImplementedException();

        public Task<TaskExecutionPlan> PlanTaskExecutionAsync(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> CanExecuteTaskAsync(
            DomainAgent agent, ICrewTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
