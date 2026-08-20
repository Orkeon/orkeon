using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Crew;
using Orkeon.Infrastructure.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using AppTaskResult = Orkeon.Application.Interfaces.Services.TaskResult;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Autonomous process strategy: agents self-organise, pick their own tasks,
/// delegate recursively to peers or freshly-spawned sub-agents, and communicate
/// via <see cref="IAgentChannel"/> — all under strict <see cref="AgentExecutionBudget"/>
/// control.
/// </summary>
/// <remarks>
/// <para>Execution flow:</para>
/// <list type="number">
///   <item>All agents register on the <see cref="IAgentChannel"/>.</item>
///   <item>Each unassigned task is claimed by the best-fit agent (LLM-scored).</item>
///   <item>The agent executes autonomously; if it decides to delegate, the budget's
///         <see cref="AgentExecutionBudget.RecordDelegation"/> is checked and a
///         child budget is derived for the delegate.</item>
///   <item>If a <see cref="BudgetExhaustedException"/> is caught the orchestrator
///         collects partial output and moves to the next task.</item>
/// </list>
/// </remarks>
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class AutonomousProcessStrategy : IProcessStrategy
{
    // Key used to propagate the derived child budget snapshot via
    // SimpleExecutionContext.Variables on delegation paths. Downstream
    // executors may read it to honour the reduced limits.
    internal const string ChildBudgetSnapshotVariable = "autonomous_child_budget_snapshot";

    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentExecutionService _executionService;
    private readonly IAgentChannel _channel;
    private readonly IManagerAgent _managerAgent;
    private readonly IMemoryScope _memoryScope;
    private readonly CrewHookDispatcher _hooks;
    private readonly ILogger<AutonomousProcessStrategy> _logger;

    /// <summary>Initializes a new instance of <see cref="AutonomousProcessStrategy"/>.</summary>
    public AutonomousProcessStrategy(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentExecutionService executionService,
        IAgentChannel channel,
        IManagerAgent managerAgent,
        IMemoryScope memoryScope,
        ILogger<AutonomousProcessStrategy> logger,
        ICrewExecutionHook? hook = null)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
        ArgumentNullException.ThrowIfNull(managerAgent);
        _managerAgent = managerAgent;
        ArgumentNullException.ThrowIfNull(memoryScope);
        _memoryScope = memoryScope;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _hooks = new CrewHookDispatcher(hook, logger);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use ExecuteAutonomousAsync for autonomous orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use ExecuteAutonomousAsync for autonomous orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use ExecuteAutonomousAsync for autonomous orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteAutonomousAsync(
        DomainCrew crew,
        AgentExecutionBudget budget,
        IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(budget);
        return ExecuteAutonomousCoreAsync(crew, budget, inputVariables, cancellationToken);
    }

    private async Task<DomainCrewOutput> ExecuteAutonomousCoreAsync(
        DomainCrew crew,
        AgentExecutionBudget budget,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingAutonomousExecution(crew.Id, budget.MaxToolCalls, budget.MaxDelegationDepth);

        var startTime = DateTime.UtcNow;
        var agents = await LoadAgentsAsync(crew).ConfigureAwait(false);

        if (agents.Count == 0)
            throw new InvalidOperationException("No agents available for autonomous execution.");

        // Token telemetry propagation (R10.8) — same metadata channel as Sequential.
        // Thread-safe: A2A channel handlers record delegated executions concurrently.
        var tokenTally = new TokenUsageTally();

        // Register all agents on the channel
        var registrations = RegisterAgentsOnChannel(agents, budget, tokenTally);

        var variables = inputVariables != null
            ? new Dictionary<string, string>(inputVariables)
            : [];

        var applicationOutputs = new List<ApplicationTaskOutput>();
        var domainResults = new List<TaskOutput>();

        try
        {
            foreach (var taskId in crew.Tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                budget.AssertWallTime();

                var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
                if (task is null)
                {
                    LogTaskNotFound(taskId);
                    continue;
                }

                var (domainOutput, appOutput) = await ExecuteTaskAutonomouslyAsync(
                    new AutonomousTaskContext(task, agents, budget, crew.Id, variables, applicationOutputs, tokenTally),
                    cancellationToken)
                    .ConfigureAwait(false);

                domainResults.Add(domainOutput);
                applicationOutputs.Add(appOutput);

                // Guard snapshot allocation: only materialize when the log level is enabled
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var snapshot = budget.ToSnapshot();
                    LogTaskCompleted(taskId, domainOutput.Success, snapshot);
                }
            }
        }
        catch (BudgetExhaustedException ex)
        {
            LogBudgetExhausted(crew.Id, ex.Dimension, ex.Message);
        }
        finally
        {
            // Unregister all agents
            foreach (var reg in registrations)
                reg.Dispose();
        }

        return await BuildOutputAsync(domainResults, agents, crew, startTime, budget, tokenTally).ConfigureAwait(false);
    }

    // ── Private methods ──────────────────────────────────────────────────

    private async Task<List<DomainAgent>> LoadAgentsAsync(DomainCrew crew)
    {
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId).ConfigureAwait(false);
            if (agent is not null)
                agents.Add(agent);
        }
        return agents;
    }

    private List<IDisposable> RegisterAgentsOnChannel(
        List<DomainAgent> agents,
        AgentExecutionBudget budget,
        TokenUsageTally tokenTally)
    {
        var registrations = new List<IDisposable>();
        foreach (var agent in agents)
        {
            var capturedAgent = agent;
            var reg = _channel.RegisterHandler(agent.Id, async (request, ct) =>
            {
                // Handle delegation requests from peers
                if (request.Intent == "delegate")
                {
                    try
                    {
                        // Derive a child budget from the parent. This represents the
                        // reduced allowance for the delegated execution. We propagate
                        // it through the execution context so downstream components
                        // can honour it, and we use it to short-circuit early if the
                        // wall-time is already exhausted.
                        var childBudget = budget.CreateChildBudget();
                        childBudget.AssertWallTime();

                        var delegatedTask = CrewTask.Create(
                            TaskDescription.From(request.Payload),
                            ExpectedOutput.From("Complete the delegated task"));

                        var delegationVariables = new Dictionary<string, string>
                        {
                            ["delegation_context"] = request.Payload,
                            [ChildBudgetSnapshotVariable] = FormatBudgetSnapshot(childBudget.ToSnapshot())
                        };

                        var context = new SimpleExecutionContext(
                            CrewId.Create(),
                            delegationVariables,
                            _memoryScope, [], ct);

                        var result = await _executionService.ExecuteTaskAsync(
                            capturedAgent, delegatedTask, context, ct).ConfigureAwait(false);

                        // Account for the tokens consumed by the delegated task on
                        // both the child budget (so its snapshot reflects reality)
                        // and the parent budget (so the overall cap is honoured).
                        if (result.TokensUsed > 0)
                        {
                            childBudget.RecordTokens(result.TokensUsed);
                            budget.RecordTokens(result.TokensUsed);
                        }

                        // Crew-level telemetry: delegated executions cost real tokens
                        // even though only their payload travels back over the channel.
                        tokenTally.Record(result);

                        return AgentChannelResponse.Ok(request.CorrelationId, capturedAgent.Id, result.Output);
                    }
                    catch (BudgetExhaustedException ex)
                    {
                        return AgentChannelResponse.Fail(
                            request.CorrelationId, capturedAgent.Id,
                            $"Budget exhausted: {ex.Message}");
                    }
                }

                // Handle info/clarification requests
                return AgentChannelResponse.Ok(
                    request.CorrelationId, capturedAgent.Id,
                    $"Agent {capturedAgent.Role} acknowledges: {request.Intent}");
            });
            registrations.Add(reg);
        }
        return registrations;
    }

    /// <summary>
    /// Groups the per-task execution dependencies of the autonomous loop (tames long
    /// argument lists, S107). <see cref="TokenTally"/> carries the crew-level token
    /// telemetry (R10.8).
    /// </summary>
    private sealed record AutonomousTaskContext(
        CrewTask Task,
        List<DomainAgent> Agents,
        AgentExecutionBudget Budget,
        CrewId CrewId,
        Dictionary<string, string> Variables,
        List<ApplicationTaskOutput> PreviousOutputs,
        TokenUsageTally TokenTally);

    private async Task<(TaskOutput Domain, ApplicationTaskOutput App)> ExecuteTaskAutonomouslyAsync(
        AutonomousTaskContext taskContext,
        CancellationToken cancellationToken)
    {
        var (task, agents, budget, crewId, variables, previousOutputs, tokenTally) = taskContext;

        // Let the LLM-based manager pick the best agent
        var context = new SimpleExecutionContext(
            crewId, variables, _memoryScope, previousOutputs, cancellationToken);

        var assignment = await _managerAgent.AssignTaskAsync(task, agents, context).ConfigureAwait(false);
        var agent = agents.FirstOrDefault(a => a.Id == assignment.AssignedAgent)
                    ?? agents[0]; // fallback to first

        LogAgentClaimedTask(agent.Id, task.Id, assignment.Reason);

        try
        {
            budget.RecordToolCall(); // count the LLM call for assignment

            var result = await _executionService.ExecuteTaskAsync(
                agent, task, context, cancellationToken).ConfigureAwait(false);

            // The direct attempt consumed tokens even when it fails and delegation
            // takes over below — record it before any branching.
            tokenTally.Record(result);

            // If the agent wants to delegate and budget allows, try recursive delegation
            if (!result.Success && agent.AllowDelegation && budget.CurrentDelegationDepth < budget.MaxDelegationDepth)
            {
                return await AttemptDelegationAsync(
                    task, agent, agents, budget).ConfigureAwait(false);
            }

            return (
                BuildDomainTaskOutput(task, result),
                BuildAppTaskOutput(task, agent, result));
        }
        catch (BudgetExhaustedException)
        {
            // Return partial output
            var partialOutput = TaskOutput.Create(
                rawOutput: $"[BUDGET EXHAUSTED] Task {task.Id} was interrupted.",
                format: "text", formattedOutput: null,
                taskId: task.Id, success: false,
                executionTime: budget.Elapsed);

            var partialApp = new ApplicationTaskOutput(
                TaskId: task.Id.Value.ToString(),
                AgentId: agent.Id.ToString(),
                Content: partialOutput.Output,
                CompletedAt: DateTime.UtcNow,
                Success: false, ExecutionTime: budget.Elapsed,
                ToolsUsed: []);

            return (partialOutput, partialApp);
        }
    }

    private async Task<(TaskOutput, ApplicationTaskOutput)> AttemptDelegationAsync(
        CrewTask task,
        DomainAgent originalAgent,
        List<DomainAgent> agents,
        AgentExecutionBudget budget)
    {
        budget.RecordDelegation();

        // Find a different agent to delegate to
        var candidates = agents.Where(a => a.Id != originalAgent.Id).ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("No delegation candidates available.");

        // Use channel for delegation (request/response)
        var delegateAgent = candidates[0];
        var request = AgentChannelRequest.Create(
            originalAgent.Id,
            delegateAgent.Id,
            "delegate",
            task.Description.Value);

        LogDelegation(originalAgent.Id, delegateAgent.Id, task.Id, budget.CurrentDelegationDepth);

        var response = await _channel.RequestAsync(
            request,
            timeout: TimeSpan.FromMinutes(2))
            .ConfigureAwait(false);

        var output = TaskOutput.Create(
            rawOutput: response.Success ? response.Payload : $"[DELEGATION FAILED] {response.Error}",
            format: "text", formattedOutput: null,
            taskId: task.Id, success: response.Success,
            executionTime: budget.Elapsed);

        var appOutput = new ApplicationTaskOutput(
            TaskId: task.Id.Value.ToString(),
            AgentId: delegateAgent.Id.ToString(),
            Content: output.Output,
            CompletedAt: DateTime.UtcNow,
            Success: response.Success,
            ExecutionTime: budget.Elapsed,
            ToolsUsed: []);

        return (output, appOutput);
    }

    /// <summary>
    /// Formats a budget snapshot into a compact, log-friendly string.
    /// Used to propagate budget limits across execution contexts via
    /// <see cref="SimpleExecutionContext.Variables"/>.
    /// </summary>
    private static string FormatBudgetSnapshot(BudgetSnapshot snapshot) =>
        $"tool_calls={snapshot.ToolCalls}/{snapshot.MaxToolCalls};" +
        $"delegation_depth={snapshot.DelegationDepth}/{snapshot.MaxDelegationDepth};" +
        $"tokens={snapshot.TokensConsumed}/{snapshot.MaxTokensConsumed};" +
        $"spawned={snapshot.SpawnedAgents}/{snapshot.MaxSpawnedAgents};" +
        $"wall_time={snapshot.Elapsed:g}/{snapshot.MaxWallTime:g}";

    private static TaskOutput BuildDomainTaskOutput(CrewTask task, AppTaskResult result)
    {
        return TaskOutput.Create(
            rawOutput: result.Output,
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: result.Success,
            executionTime: result.ExecutionTime,
            structuredOutput: result.StructuredOutput);
    }

    private static ApplicationTaskOutput BuildAppTaskOutput(
        CrewTask task, DomainAgent agent, AppTaskResult result)
    {
        return new ApplicationTaskOutput(
            TaskId: task.Id.Value.ToString(),
            AgentId: agent.Id.ToString(),
            Content: result.Output,
            CompletedAt: DateTime.UtcNow,
            Success: result.Success,
            ExecutionTime: result.ExecutionTime,
            ToolsUsed: result.ToolsUsed);
    }

    private async Task<DomainCrewOutput> BuildOutputAsync(
        List<TaskOutput> results,
        List<DomainAgent> agents,
        DomainCrew crew,
        DateTime startTime,
        AgentExecutionBudget budget,
        TokenUsageTally tokenTally)
    {
        var finalOutput = string.Join("\n\n", results.Select(r => r.Output));
        var totalTime = DateTime.UtcNow - startTime;
        var snapshot = budget.ToSnapshot();

        LogAutonomousExecutionCompleted(crew.Id, totalTime, snapshot.ToolCalls, snapshot.DelegationDepth);

        var metadata = tokenTally
            .WriteTo(Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder()
                .Add("process_type", "autonomous")
                .Add("agent_count", agents.Count)
                .Add("budget_tool_calls", $"{snapshot.ToolCalls}/{snapshot.MaxToolCalls}")
                .Add("budget_delegation_depth", $"{snapshot.DelegationDepth}/{snapshot.MaxDelegationDepth}")
                .Add("budget_tokens", $"{snapshot.TokensConsumed}/{snapshot.MaxTokensConsumed}")
                .Add("budget_spawned", $"{snapshot.SpawnedAgents}/{snapshot.MaxSpawnedAgents}")
                .Add("budget_exhausted", snapshot.IsExhausted))
            .Build();

        // Autonomous agents delegate and spawn: there is no ordered task loop to hook
        // into, so the outcome is reported when the run settles. The budget snapshot is
        // what makes that report worth reading here.
        var taskSnapshots = new List<TaskExecutionSnapshot>(results.Count);
        foreach (var result in results)
        {
            var taskSnapshot = new TaskExecutionSnapshot
            {
                TaskId = result.TaskId?.Value.ToString() ?? string.Empty,
                AgentRole = "autonomous",
                Success = result.Success,
                Duration = result.ExecutionTime,
                CompletedAt = DateTimeOffset.UtcNow,
                ToolCallCount = snapshot.ToolCalls,
            };
            taskSnapshots.Add(taskSnapshot);
            await _hooks.TaskCompletedAsync(taskSnapshot).ConfigureAwait(false);
        }

        await _hooks.CrewCompletedAsync(
            CrewHookDispatcher.Snapshot(
                crew.Id.ToString(), startTime, taskSnapshots,
                snapshot.IsExhausted ? CrewHookStatus.Canceled : CrewHookStatus.Completed,
                snapshot.IsExhausted ? "Execution budget exhausted" : null))
            .ConfigureAwait(false);

        return DomainCrewOutput.CreateSuccess(
            output: finalOutput,
            structuredOutput: null,
            taskOutputs: results,
            executionTime: totalTime,
            metadata: metadata);
    }

    // ── Logging ──────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting autonomous execution for crew {CrewId} (budget: {MaxToolCalls} tool calls, depth {MaxDepth})")]
    private partial void LogStartingAutonomousExecution(CrewId crewId, int maxToolCalls, int maxDepth);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {TaskId} not found, skipping")]
    private partial void LogTaskNotFound(TaskId taskId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent {AgentId} claimed task {TaskId}: {Reason}")]
    private partial void LogAgentClaimedTask(AgentId agentId, TaskId taskId, string reason);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Delegation: {From} → {To} for task {TaskId} (depth: {Depth})")]
    private partial void LogDelegation(AgentId from, AgentId to, TaskId taskId, int depth);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Task {TaskId} completed (success: {Success}, budget: {Snapshot})")]
    private partial void LogTaskCompleted(TaskId taskId, bool success, BudgetSnapshot snapshot);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Budget exhausted for crew {CrewId}: dimension={Dimension}, {Message}")]
    private partial void LogBudgetExhausted(CrewId crewId, BudgetDimension dimension, string message);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Autonomous execution completed for crew {CrewId} in {Duration} (tool calls: {ToolCalls}, delegation depth: {Depth})")]
    private partial void LogAutonomousExecutionCompleted(CrewId crewId, TimeSpan duration, int toolCalls, int depth);
}
