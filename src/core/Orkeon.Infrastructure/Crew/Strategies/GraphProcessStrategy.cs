using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Graph;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Infrastructure.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Domain.Autonomous;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// LangGraph-style process strategy that builds a typed state graph
/// with conditional edges and controlled cycles for crew execution.
///
/// Graph topology:
/// START → agent_execute → route_decision →(loop back to agent_execute OR → END)
///
/// The route_decision node inspects the accumulated state to decide
/// whether to loop (e.g., retry failed tasks, handle delegation) or finish.
/// Cycle protection is enforced by <see cref="CircuitBreakerPolicy"/>.
/// </summary>
public sealed partial class GraphProcessStrategy : IProcessStrategy
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentExecutionService _executionService;
    private readonly IMemoryScope _memoryScope;
    private readonly CrewHookDispatcher _hooks;
    private readonly AgentDelegationToolsProvider _delegationProvider;
    private readonly ILogger<GraphProcessStrategy> _logger;

    /// <summary>
    /// Fallback circuit breaker policy used when the crew carries no <see cref="Domain.Configuration.GraphConfig"/>
    /// (nor a crew-level circuit-breaker config). Defaults to Strict. Per-crew config, when present,
    /// takes precedence and is resolved off the crew at execution time — never stored on this
    /// (scoped, potentially shared) strategy instance.
    /// </summary>
    public CircuitBreakerPolicy CircuitPolicy { get; init; } = CircuitBreakerPolicy.Strict;

    /// <summary>
    /// Fallback maximum retry cycles for failed tasks, used when the crew carries no
    /// <see cref="Domain.Configuration.GraphConfig"/>. Applied in addition to the circuit breaker.
    /// </summary>
    public int MaxRetryCycles { get; init; } = 2;

    /// <summary>
    /// Creates a new <see cref="GraphProcessStrategy"/>.
    /// </summary>
    public GraphProcessStrategy(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope,
        AgentDelegationToolsProvider delegationProvider,
        ILogger<GraphProcessStrategy> logger,
        ICrewExecutionHook? hook = null)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
        ArgumentNullException.ThrowIfNull(memoryScope);
        _memoryScope = memoryScope;
        ArgumentNullException.ThrowIfNull(delegationProvider);
        _delegationProvider = delegationProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _hooks = new CrewHookDispatcher(hook, logger);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteSequentialAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(plan);
        return ExecuteSequentialCoreAsync(crew, plan, inputVariables);
    }

    private async Task<DomainCrewOutput> ExecuteSequentialCoreAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables)
    {
        LogStartingGraphExecution(crew.Id);
        var startTime = DateTime.UtcNow;

        // Load agents
        var agents = await LoadAgentsAsync(crew).ConfigureAwait(false);
        if (agents.Count == 0 && crew.Tasks.Count > 0)
            throw new InvalidOperationException("No agents available for graph execution.");

        foreach (var agent in agents)
        {
            _delegationProvider.RegisterAgentEntity(agent);
            _delegationProvider.AddDelegationToolsToAgent(agent);
        }

        var variables = inputVariables != null
            ? new Dictionary<string, string>(inputVariables)
            : [];

        var taskIds = GetOrderedTaskIds(crew, plan).ToList();

        // Resolve the effective graph config off the crew (P2-O-01): per-crew GraphConfig wins,
        // then a crew-level circuit-breaker config, then this strategy's fallback defaults. The
        // config travels with the crew argument, not on the shared scoped strategy, so concurrent
        // crews can never clobber one another's policy.
        var effectivePolicy = CircuitBreakerPolicyFactory.ResolveGraph(
            crew.GraphConfig, crew.CircuitBreaker, CircuitPolicy);
        var effectiveMaxRetryCycles = crew.GraphConfig?.MaxRetryCycles ?? MaxRetryCycles;

        // Build the initial graph state
        var initialState = new CrewGraphState
        {
            CrewId = crew.Id,
            Agents = agents,
            PendingTaskIds = new Queue<TaskId>(taskIds),
            Variables = variables,
            TotalTokensUsed = 0,
            RetryCounts = new Dictionary<string, int>(),
            MaxRetryCycles = effectiveMaxRetryCycles
        };

        // Build and compile the state graph
        var graph = BuildCrewGraph(initialState, effectivePolicy);
        var runner = graph.Compile();

        // Wire up observability
        runner.OnNodeCompleted += (_, args) =>
        {
            LogNodeCompleted(args.NodeName, args.TransitionOrdinal);
        };

        runner.OnCircuitBroken += (_, args) =>
        {
            LogCircuitBroken(args.Reason, args.NodeName, args.TransitionCount);
        };

        try
        {
            var result = await runner.RunAsync(initialState, CancellationToken.None).ConfigureAwait(false);
            var finalState = result.FinalState;
            var totalTime = DateTime.UtcNow - startTime;

            LogGraphExecutionCompleted(crew.Id, totalTime, result.TotalTransitions, result.Trace);

            var domainResults = finalState.DomainResults;
            var finalOutput = (domainResults.Count > 0 ? domainResults[^1] : null)?.Output ?? string.Empty;

            // A graph's nodes are not tasks, so there is no per-task moment to hook into
            // mid-run: the results are reported when the graph joins.
            var snapshots = await NotifyResultsAsync(domainResults).ConfigureAwait(false);
            await _hooks.CrewCompletedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, snapshots, CrewHookStatus.Completed))
                .ConfigureAwait(false);

            return DomainCrewOutput.CreateSuccess(
                output: finalOutput,
                structuredOutput: null,
                taskOutputs: finalState.DomainResults,
                executionTime: totalTime,
                metadata: BuildTokenMetadata(finalState));
        }
        catch (GraphCircuitBrokenException ex)
        {
            var totalTime = DateTime.UtcNow - startTime;
            LogGraphExecutionCircuitBroken(crew.Id, ex.Message);

            // The graph nodes mutate the state instance in place, so initialState carries
            // the tokens consumed up to the break — propagate them, they were paid for.
            var brokenSnapshots = await NotifyResultsAsync(initialState.DomainResults).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, brokenSnapshots, CrewHookStatus.Failed, ex.Message),
                ex).ConfigureAwait(false);

            return DomainCrewOutput.CreateFailure(
                error: $"Graph execution stopped by circuit breaker: {ex.Message}",
                taskOutputs: initialState.DomainResults,
                executionTime: totalTime,
                metadata: BuildTokenMetadata(initialState));
        }
    }

    /// <summary>
    /// Surfaces the token counting accumulated in the graph state to the crew metadata
    /// using the canonical keys, so the orchestrator can rebuild a real token usage
    /// (R10.8 — the internal count previously never left the state).
    /// </summary>
    /// <summary>
    /// Reports each produced result to the hook and returns the snapshots, so the
    /// crew-level event carries exactly what the per-task events announced.
    /// </summary>
    private async Task<List<TaskExecutionSnapshot>> NotifyResultsAsync(
        IReadOnlyList<DomainTaskOutput> results)
    {
        var snapshots = new List<TaskExecutionSnapshot>(results.Count);
        foreach (var result in results)
        {
            var snapshot = new TaskExecutionSnapshot
            {
                TaskId = result.TaskId.Value.ToString(),
                AgentRole = "graph",
                Success = result.Success,
                Duration = result.ExecutionTime,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            snapshots.Add(snapshot);
            await _hooks.TaskCompletedAsync(snapshot).ConfigureAwait(false);
        }

        return snapshots;
    }

    private static Orkeon.Domain.Crew.ValueObjects.CrewMetadata BuildTokenMetadata(CrewGraphState state)
        => TokenUsageTally.WriteTo(
                Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder(),
                state.TotalTokensUsed,
                state.PromptTokensUsed,
                state.CompletionTokensUsed)
            .Build();

    /// <summary>
    /// Builds the LangGraph-style state graph for crew execution.
    ///
    /// Topology:
    ///   START → execute_task → route → [execute_task | END]
    ///
    /// The "route" node checks if there are pending tasks or failed tasks to retry.
    /// </summary>
    private StateGraph<CrewGraphState> BuildCrewGraph(CrewGraphState initialState, CircuitBreakerPolicy policy)
    {
        var context = new SimpleExecutionContext(
            initialState.CrewId,
            initialState.Variables,
            _memoryScope,
            initialState.ApplicationOutputs,
            CancellationToken.None);

        _delegationProvider.UpdateExecutionContext(context);

        var graph = new StateGraph<CrewGraphState>(policy);

        // Node: execute_task — delegates the heavy lifting to a dedicated method
        // to keep this builder's cognitive complexity low.
        graph.AddNode("execute_task", ExecuteTaskNodeAsync);

        // Node: route — decides whether to continue executing or finish.
        graph.AddNode("route", RouteNodeAsync);

        // Edges
        graph.AddEdge(StateGraph<CrewGraphState>.StartNode, "execute_task");
        graph.AddEdge("execute_task", "route");

        graph.AddConditionalEdge("route",
            SelectNextNodeFromRoute,
            ["execute_task", StateGraph<CrewGraphState>.EndNode]);

        return graph;
    }

    /// <summary>
    /// Executes the next pending task in the state queue (graph node body).
    /// This is the core transition logic of the execute_task node.
    /// </summary>
    private async Task<CrewGraphState> ExecuteTaskNodeAsync(CrewGraphState state, CancellationToken ct)
    {
        if (state.PendingTaskIds.Count == 0)
            return state; // Nothing to do, will route to END

        var taskId = state.PendingTaskIds.Dequeue();
        LogExecutingTask(taskId);

        var task = await _taskRepository.GetByIdAsync(taskId, ct).ConfigureAwait(false);
        if (task == null)
        {
            LogTaskNotFound(taskId);
            return state;
        }

        var agent = SelectAgent(task, state.Agents, state.AgentIndex);
        state.AgentIndex++;

        var executionResult = await RunTaskAsync(state, task, agent, ct).ConfigureAwait(false);

        AppendTaskOutputs(state, task, agent, executionResult);
        EvaluateFailureForRetry(state, taskId, executionResult);

        LogTaskCompleted(taskId, executionResult.Success);
        return state;
    }

    /// <summary>
    /// Rebuilds the execution context with the latest outputs and runs the task.
    /// </summary>
    private async Task<TaskResult> RunTaskAsync(
        CrewGraphState state,
        Orkeon.Domain.Task.CrewTask task,
        DomainAgent agent,
        CancellationToken ct)
    {
        var execContext = new SimpleExecutionContext(
            state.CrewId,
            state.Variables,
            _memoryScope,
            state.ApplicationOutputs,
            ct);
        _delegationProvider.UpdateExecutionContext(execContext);

        return await _executionService.ExecuteTaskAsync(
            agent, task, execContext, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends the application-level and domain-level task outputs to the state
    /// and updates the running token total.
    /// </summary>
    private static void AppendTaskOutputs(
        CrewGraphState state,
        Orkeon.Domain.Task.CrewTask task,
        DomainAgent agent,
        TaskResult executionResult)
    {
        var rawOutput = GetRawOutput(executionResult);
        state.TotalTokensUsed += executionResult.TokensUsed;
        state.PromptTokensUsed += executionResult.PromptTokens;
        state.CompletionTokensUsed += executionResult.CompletionTokens;

        var appOutput = new ApplicationTaskOutput(
            TaskId: task.Id.Value.ToString(),
            AgentId: agent.Id.ToString(),
            Content: rawOutput,
            CompletedAt: DateTime.UtcNow,
            Success: executionResult.Success,
            ExecutionTime: executionResult.ExecutionTime,
            ToolsUsed: executionResult.ToolsUsed);
        state.AddApplicationOutput(appOutput);

        state.AddDomainResult(DomainTaskOutput.Create(
            rawOutput: rawOutput,
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: executionResult.Success,
            executionTime: executionResult.ExecutionTime,
            structuredOutput: executionResult.StructuredOutput));
    }

    /// <summary>
    /// Tracks failure for the task and enqueues it for retry if cycles remain.
    /// </summary>
    private void EvaluateFailureForRetry(
        CrewGraphState state,
        TaskId taskId,
        TaskResult executionResult)
    {
        if (executionResult.Success)
            return;

        var taskKey = taskId.Value.ToString();
        state.RetryCounts.TryGetValue(taskKey, out var retries);
        var nextAttempt = retries + 1;
        state.RetryCounts[taskKey] = nextAttempt;

        if (nextAttempt <= state.MaxRetryCycles)
        {
            state.FailedTaskIds.Enqueue(taskId);
            LogTaskFailedWillRetry(taskId, nextAttempt, state.MaxRetryCycles);
        }
        else
        {
            LogTaskFailedMaxRetries(taskId, state.MaxRetryCycles);
        }
    }

    /// <summary>
    /// Route node body: promotes failed tasks back to the pending queue when
    /// no pending tasks remain, enabling the controlled retry cycle.
    /// </summary>
    private Task<CrewGraphState> RouteNodeAsync(CrewGraphState state, CancellationToken _)
    {
        if (state.PendingTaskIds.Count == 0 && state.FailedTaskIds.Count > 0)
        {
            LogPromotingFailedTasks(state.FailedTaskIds.Count);
            while (state.FailedTaskIds.Count > 0)
                state.PendingTaskIds.Enqueue(state.FailedTaskIds.Dequeue());
        }

        return Task.FromResult(state);
    }

    /// <summary>
    /// Conditional edge evaluator for the route node: decides whether to loop
    /// back into execution or complete the graph traversal.
    /// </summary>
    private static string SelectNextNodeFromRoute(CrewGraphState state)
        => state.PendingTaskIds.Count > 0
            ? "execute_task"
            : StateGraph<CrewGraphState>.EndNode;

    #region Helpers

    private async Task<List<DomainAgent>> LoadAgentsAsync(DomainCrew crew)
    {
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId).ConfigureAwait(false);
            if (agent != null) agents.Add(agent);
        }
        return agents;
    }

    private static IEnumerable<TaskId> GetOrderedTaskIds(DomainCrew crew, DomainExecutionPlan plan)
    {
        var plannedTasks = plan.GetTasksInOrder().ToList();
        return plannedTasks.Count > 0
            ? plannedTasks.Select(pt => pt.TaskId)
            : crew.Tasks;
    }

    private static DomainAgent SelectAgent(
        Orkeon.Domain.Task.CrewTask task,
        IReadOnlyList<DomainAgent> agents,
        int agentIndex)
    {
        return task.AssignedAgent != null
            ? agents.FirstOrDefault(a => a.Id == task.AssignedAgent) ?? agents[agentIndex % agents.Count]
            : agents[agentIndex % agents.Count];
    }

    private static string GetRawOutput(TaskResult result)
    {
        if (!string.IsNullOrEmpty(result.Output))
            return result.Output;
        return result.Success ? "(no output)" : $"Task failed: {result.Error ?? "unknown error"}";
    }

    #endregion

    #region Unsupported process types

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(
        DomainCrew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Hierarchical execution is not supported by GraphProcessStrategy. " +
            "Use HierarchicalProcessStrategy instead.");
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(
        DomainCrew crew, DomainExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Parallel execution is not supported by GraphProcessStrategy. " +
            "Use ParallelProcessStrategy instead.");
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    #endregion

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting graph-based execution for crew {CrewId}")]
    private partial void LogStartingGraphExecution(CrewId crewId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Graph node '{NodeName}' completed (transition #{Ordinal})")]
    private partial void LogNodeCompleted(string nodeName, int ordinal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graph circuit breaker tripped: {Reason} at node '{NodeName}' after {TransitionCount} transitions")]
    private partial void LogCircuitBroken(string reason, string nodeName, int transitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graph execution completed for crew {CrewId} in {Duration} ({Transitions} transitions, trace: {Trace})")]
    private partial void LogGraphExecutionCompleted(CrewId crewId, TimeSpan duration, int transitions, IReadOnlyList<string> trace);

    [LoggerMessage(Level = LogLevel.Error, Message = "Graph execution for crew {CrewId} stopped by circuit breaker: {Message}")]
    private partial void LogGraphExecutionCircuitBroken(CrewId crewId, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing task {TaskId}")]
    private partial void LogExecutingTask(TaskId taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {TaskId} not found, skipping")]
    private partial void LogTaskNotFound(TaskId taskId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Task {TaskId} completed, success: {Success}")]
    private partial void LogTaskCompleted(TaskId taskId, bool success);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {TaskId} failed (attempt {Attempt}/{MaxRetries}), will retry")]
    private partial void LogTaskFailedWillRetry(TaskId taskId, int attempt, int maxRetries);

    [LoggerMessage(Level = LogLevel.Error, Message = "Task {TaskId} failed after {MaxRetries} retries, giving up")]
    private partial void LogTaskFailedMaxRetries(TaskId taskId, int maxRetries);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Promoting {Count} failed tasks for retry cycle")]
    private partial void LogPromotingFailedTasks(int count);

    #endregion
}

/// <summary>
/// Typed state flowing through the crew execution graph.
/// Accumulates task outputs, tracks failures, and carries context.
/// </summary>
public sealed class CrewGraphState
{
    private readonly List<ApplicationTaskOutput> _applicationOutputs = [];
    private readonly List<DomainTaskOutput> _domainResults = [];

    /// <summary>The crew being executed.</summary>
    public required CrewId CrewId { get; init; }

    /// <summary>Available agents.</summary>
    public required IReadOnlyList<DomainAgent> Agents { get; init; }

    /// <summary>Tasks remaining to execute.</summary>
    public required Queue<TaskId> PendingTaskIds { get; init; }

    /// <summary>Tasks that failed and are eligible for retry.</summary>
    public Queue<TaskId> FailedTaskIds { get; init; } = new();

    /// <summary>Input variables for template interpolation.</summary>
    public required Dictionary<string, string> Variables { get; init; }

    /// <summary>Accumulated application-level task outputs.</summary>
    public IReadOnlyList<ApplicationTaskOutput> ApplicationOutputs => _applicationOutputs;

    /// <summary>Accumulated domain-level task outputs.</summary>
    public IReadOnlyList<DomainTaskOutput> DomainResults => _domainResults;

    /// <summary>Appends an application-level task output to the accumulated results.</summary>
    public void AddApplicationOutput(ApplicationTaskOutput output) => _applicationOutputs.Add(output);

    /// <summary>Appends a domain-level task output to the accumulated results.</summary>
    public void AddDomainResult(DomainTaskOutput result) => _domainResults.Add(result);

    /// <summary>Running total of tokens used.</summary>
    public int TotalTokensUsed { get; set; }

    /// <summary>Running total of prompt-side tokens (0 when the provider does not report the split).</summary>
    public int PromptTokensUsed { get; set; }

    /// <summary>Running total of completion-side tokens (0 when the provider does not report the split).</summary>
    public int CompletionTokensUsed { get; set; }

    /// <summary>Round-robin agent index.</summary>
    public int AgentIndex { get; set; }

    /// <summary>Per-task retry counts (taskId string → count).</summary>
    public required Dictionary<string, int> RetryCounts { get; init; }

    /// <summary>Max retry cycles per failed task.</summary>
    public required int MaxRetryCycles { get; init; }
}
