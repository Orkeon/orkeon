using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Tools.Abstractions.Base;
using ITool = Orkeon.Domain.Common.ITool;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tools;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Request parameters for the spawn agent tool.</summary>
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public record SpawnAgentRequest
{
    /// <summary>Gets the role for the new sub-agent.</summary>
    [FieldSchema(Description = "The role for the new sub-agent", Example = "data_analyst")]
    public string Role { get; init; } = "";

    /// <summary>Gets the goal for the new sub-agent.</summary>
    [FieldSchema(Description = "The goal for the new sub-agent", Example = "Analyze sales data and produce a trend report")]
    public string Goal { get; init; } = "";

    /// <summary>Gets the optional backstory for the new sub-agent.</summary>
    [FieldSchema(Description = "Optional backstory providing context", IsRequired = false)]
    public string Backstory { get; init; } = "";

    /// <summary>Gets whether the spawned agent can itself delegate work.</summary>
    [FieldSchema(Description = "Whether the spawned agent can delegate (default: false)", IsRequired = false)]
    public bool AllowDelegation { get; init; }

    /// <summary>Gets the task to assign to the spawned agent immediately.</summary>
    [FieldSchema(Description = "Task description for immediate execution", Example = "Analyze Q4 revenue by region")]
    public string Task { get; init; } = "";

    /// <summary>Gets whether to wait for the spawned agent to complete its task.</summary>
    [FieldSchema(Description = "If true, waits for the spawned agent to complete and returns its output", IsRequired = false)]
    public bool WaitForResult { get; init; } = true;
}

/// <summary>Response from the spawn agent tool.</summary>
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public record SpawnAgentResponse
{
    /// <summary>Gets the ID of the spawned agent.</summary>
    [ReturnSchema(Description = "ID of the spawned agent")]
    public string SpawnedAgentId { get; init; } = "";

    /// <summary>Gets the role of the spawned agent.</summary>
    [ReturnSchema(Description = "Role of the spawned agent")]
    public string SpawnedAgentRole { get; init; } = "";

    /// <summary>Gets a confirmation or result message.</summary>
    [ReturnSchema(Description = "Confirmation message or execution result")]
    public string Message { get; init; } = "";

    /// <summary>Gets whether the spawned task succeeded (only when WaitForResult=true).</summary>
    [ReturnSchema(Description = "Whether the spawned agent's task succeeded")]
    public bool Success { get; init; }

    /// <summary>Gets the spawned agent's output (only when WaitForResult=true).</summary>
    [ReturnSchema(Description = "Output from the spawned agent")]
    public string Result { get; init; } = "";

    /// <summary>Gets the execution time in milliseconds (only when WaitForResult=true).</summary>
    [ReturnSchema(Description = "Execution time in milliseconds")]
    public long ExecutionTimeMs { get; init; }
}

/// <summary>
/// Tool that allows autonomous agents to spawn sub-agents at runtime.
/// Each spawn is recorded against the parent's <see cref="AgentExecutionBudget"/>
/// and the child receives a derived budget with reduced limits.
/// </summary>
[ToolContract("spawn_agent", Name = "Spawn sub-agent",
    Description = "Dynamically create and execute a specialised sub-agent for a specific task",
    Category = "Autonomous")]
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class SpawnAgentTool : ToolBase<SpawnAgentRequest, SpawnAgentResponse>, ITool
{
    private readonly IAgentFactory _agentFactory;
    private readonly AgentId _parentAgentId;
    private readonly CrewId _parentCrewId;
    private readonly AgentExecutionBudget _budget;
    private readonly Application.Interfaces.Services.IAgentExecutionService? _executionService;
    private readonly Application.Interfaces.Ports.IMemoryScope? _memoryScope;

    /// <summary>Initializes a new instance of <see cref="SpawnAgentTool"/>.</summary>
    public SpawnAgentTool(
        IAgentFactory agentFactory,
        AgentId parentAgentId,
        CrewId parentCrewId,
        AgentExecutionBudget budget,
        Application.Interfaces.Services.IAgentExecutionService? executionService = null,
        Application.Interfaces.Ports.IMemoryScope? memoryScope = null,
        ILogger<SpawnAgentTool>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        _agentFactory = agentFactory;
        ArgumentNullException.ThrowIfNull(parentAgentId);
        _parentAgentId = parentAgentId;
        ArgumentNullException.ThrowIfNull(parentCrewId);
        _parentCrewId = parentCrewId;
        ArgumentNullException.ThrowIfNull(budget);
        _budget = budget;
        _executionService = executionService;
        _memoryScope = memoryScope;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(SpawnAgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Role))
            return "Role is required for the sub-agent";

        if (string.IsNullOrWhiteSpace(request.Goal))
            return "Goal is required for the sub-agent";

        if (request.WaitForResult && _executionService is null)
            return "Synchronous spawn is not available: IAgentExecutionService not configured";

        if (_budget.IsExhausted)
            return "Execution budget is exhausted — cannot spawn new agents";

        return null;
    }

    /// <inheritdoc />
    protected override Task<SpawnAgentResponse> ExecuteTypedAsync(
        SpawnAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SpawnAgentResponse> ExecuteTypedCoreAsync()
        {
            // Record the spawn against the parent budget (may throw BudgetExhaustedException)
            _budget.RecordSpawn();

            // Derive the child budget NOW so we can propagate it to the spawned agent
            // via the spawn request metadata and the execution context. This is a
            // FUNCTIONAL fix: prior to this change, childBudget was calculated but
            // never flowed to the child, meaning spawned agents effectively ran under
            // the parent's untouched limits.
            var childBudget = _budget.CreateChildBudget();
            childBudget.AssertWallTime();

            var spawnRequest = AgentSpawnRequest.CreateBuilder(
                    AgentRole.From(request.Role),
                    AgentGoal.From(request.Goal),
                    _parentCrewId)
                .WithBackstory(string.IsNullOrWhiteSpace(request.Backstory)
                    ? null
                    : AgentBackstory.From(request.Backstory))
                .AllowDelegation(request.AllowDelegation)
                .RequestedBy(_parentAgentId)
                .MaxIterations(5) // conservative for spawned agents
                .WithMetadata("spawned_by", _parentAgentId.ToString())
                .WithMetadata("budget_remaining_tool_calls", _budget.MaxToolCalls - _budget.CurrentToolCalls)
                // Propagate the derived child budget so the factory, execution service
                // and any downstream component can honour the reduced limits.
                .WithMetadata(ChildBudgetMetadataKey, childBudget)
                .WithMetadata("child_budget_max_tool_calls", childBudget.MaxToolCalls)
                .WithMetadata("child_budget_max_delegation_depth", childBudget.MaxDelegationDepth)
                .WithMetadata("child_budget_max_tokens", childBudget.MaxTokensConsumed)
                .WithMetadata("child_budget_max_spawned_agents", childBudget.MaxSpawnedAgents)
                .Build();

            var spawnedAgent = await _agentFactory.CreateAgentAsync(spawnRequest, cancellationToken)
                .ConfigureAwait(false);

            LogAgentSpawned(_parentAgentId, spawnedAgent.Id, request.Role);

            if (!request.WaitForResult || _executionService is null)
            {
                return new SpawnAgentResponse
                {
                    SpawnedAgentId = spawnedAgent.Id.ToString(),
                    SpawnedAgentRole = request.Role,
                    Message = $"Sub-agent '{request.Role}' spawned successfully (fire-and-forget).",
                    Success = true
                };
            }

            // Execute the task synchronously with child budget
            var task = Domain.Task.CrewTask.Create(
                Domain.Task.ValueObjects.TaskDescription.From(request.Task),
                Domain.Task.ValueObjects.ExpectedOutput.From(request.Goal));

            // The context variables carry the child budget snapshot so downstream
            // executors can read it (e.g. to early-cancel when a specific dimension
            // is exhausted on the child).
            var context = new Application.Context.SimpleExecutionContext(
                _parentCrewId,
                new Dictionary<string, string>
                {
                    ["parent_agent"] = _parentAgentId.ToString(),
                    ["spawn_task"] = request.Task,
                    [ChildBudgetVariableKey] = FormatBudgetLimits(childBudget)
                },
                _memoryScope ?? Orkeon.Application.Context.NullMemoryScope.Instance,
                [],
                cancellationToken);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var result = await _executionService.ExecuteTaskAsync(
                    spawnedAgent, task, context, cancellationToken).ConfigureAwait(false);

                sw.Stop();
                LogSpawnedAgentCompleted(spawnedAgent.Id, result.Success, sw.Elapsed);

                // Account for any tokens consumed by the spawned execution on BOTH
                // budgets: the child's own copy (so its snapshot reflects reality)
                // and the parent's (so the overall cap stays coherent).
                if (result.TokensUsed > 0)
                {
                    childBudget.RecordTokens(result.TokensUsed);
                    _budget.RecordTokens(result.TokensUsed);
                }

                return new SpawnAgentResponse
                {
                    SpawnedAgentId = spawnedAgent.Id.ToString(),
                    SpawnedAgentRole = request.Role,
                    Message = result.Success
                        ? $"Sub-agent '{request.Role}' completed successfully."
                        : $"Sub-agent '{request.Role}' failed: {result.Error}",
                    Success = result.Success,
                    Result = result.Output,
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }
            catch (BudgetExhaustedException ex)
            {
                sw.Stop();
                return new SpawnAgentResponse
                {
                    SpawnedAgentId = spawnedAgent.Id.ToString(),
                    SpawnedAgentRole = request.Role,
                    Message = $"Sub-agent '{request.Role}' interrupted: {ex.Message}",
                    Success = false,
                    Result = $"[BUDGET EXHAUSTED] {ex.Dimension}: {ex.Message}",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }
        }
    }

    /// <summary>Metadata key used to attach the derived child budget to an <see cref="AgentSpawnRequest"/>.</summary>
    public const string ChildBudgetMetadataKey = "child_execution_budget";

    /// <summary>Variable key used to propagate the child budget limits through <see cref="Application.Context.SimpleExecutionContext.Variables"/>.</summary>
    public const string ChildBudgetVariableKey = "spawn_child_budget_limits";

    private static string FormatBudgetLimits(AgentExecutionBudget budget) =>
        $"tool_calls={budget.MaxToolCalls};" +
        $"delegation_depth={budget.MaxDelegationDepth};" +
        $"tokens={budget.MaxTokensConsumed};" +
        $"spawned={budget.MaxSpawnedAgents};" +
        $"wall_time={budget.MaxWallTime:g}";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent {ParentId} spawned sub-agent {ChildId} (role: {Role})")]
    private partial void LogAgentSpawned(AgentId parentId, AgentId childId, string role);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Spawned agent {AgentId} completed (success: {Success}) in {Duration}")]
    private partial void LogSpawnedAgentCompleted(AgentId agentId, bool success, TimeSpan duration);
}
