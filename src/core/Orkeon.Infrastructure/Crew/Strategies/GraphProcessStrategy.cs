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
    private readonly TaskAgentSelector _agentSelector;
    private readonly TaskLifecycle _lifecycle;

    /// <summary>
    /// Circuit breaker policy used as-is when the crew carries no <see cref="Domain.Configuration.GraphConfig"/>.
    /// Null — the default — sizes the breaker from the
    /// crew instead: <c>tasks × (1 + maxRetryCycles)</c> visits of <c>execute_task</c> and twice
    /// that plus one transitions, on the Strict preset's duration (GAP-03). A fixed preset used
    /// to cap every crew: Strict's five visits failed any healthy crew of six tasks. Per-crew
    /// config, when present, takes precedence and is resolved off the crew at execution time —
    /// never stored on this (scoped, potentially shared) strategy instance.
    /// </summary>
    public CircuitBreakerPolicy? CircuitPolicy { get; init; }

    /// <summary>
    /// Fallback maximum retry cycles for failed tasks, used when the crew carries no
    /// <see cref="Domain.Configuration.GraphConfig"/>. Applied in addition to the circuit breaker.
    /// </summary>
    public int MaxRetryCycles { get; init; } = 2;

    /// <summary>
    /// Creates a new <see cref="GraphProcessStrategy"/>.
    /// </summary>
    /// <param name="dependencies">The collaborators shared by every crew strategy.</param>
    /// <param name="delegationProvider">The agent delegation tools provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="hook">Optional crew execution hook. May be null (BUS-03).</param>
    /// <param name="agentSelector">Who runs a task that names no agent. Null means round-robin.</param>
    public GraphProcessStrategy(
        CrewStrategyDependencies dependencies,
        AgentDelegationToolsProvider delegationProvider,
        ILogger<GraphProcessStrategy> logger,
        ICrewExecutionHook? hook = null,
        TaskAgentSelector? agentSelector = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _taskRepository = dependencies.TaskRepository;
        _agentRepository = dependencies.AgentRepository;
        _executionService = dependencies.ExecutionService;
        _memoryScope = dependencies.MemoryScope;
        ArgumentNullException.ThrowIfNull(delegationProvider);
        _delegationProvider = delegationProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _hooks = new CrewHookDispatcher(hook, logger);
        _agentSelector = agentSelector ?? TaskAgentSelector.RoundRobin;
        _lifecycle = dependencies.LifecycleFor(logger);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteSequentialAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return ExecuteSequentialCoreAsync(crew, inputVariables, cancellationToken);
    }

    private async Task<DomainCrewOutput> ExecuteSequentialCoreAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingGraphExecution(crew.Id);
        var startTime = DateTime.UtcNow;
        var outcome = new CrewRunOutcome(_lifecycle, cancellationToken);

        try
        {

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

        var taskIds = await CrewTaskSequencer.ResolveAsync(
            crew, _taskRepository, _logger, cancellationToken).ConfigureAwait(false);

        // Resolve the effective graph config off the crew (P2-O-01): per-crew GraphConfig wins,
        // then this strategy's fallback defaults. The
        // config travels with the crew argument, not on the shared scoped strategy, so concurrent
        // crews can never clobber one another's policy.
        var effectiveMaxRetryCycles = crew.GraphConfig?.MaxRetryCycles ?? MaxRetryCycles;
        var effectivePolicy = ResolvePolicy(crew, taskIds.Count, effectiveMaxRetryCycles);

        // Build the initial graph state
        var initialState = new CrewGraphState
        {
            CrewId = crew.Id,
            Agents = agents,
            PendingTaskIds = new Queue<TaskId>(taskIds),
            Variables = variables,
            TotalTokensUsed = 0,
            RetryCounts = new Dictionary<string, int>(),
            MaxRetryCycles = effectiveMaxRetryCycles,
            Outcome = outcome,
            // The run's context, from which each task's is derived (GAP-30, GAP-21).
            Context = new SimpleExecutionContext(crew.Id, variables, _memoryScope, [], cancellationToken),
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
            // The caller's token used to be dropped on the floor here (CancellationToken.None):
            // a graph crew could not be cancelled at all — no Ctrl+C, no host RunTimeout.
            var result = await runner.RunAsync(initialState, cancellationToken).ConfigureAwait(false);
            var finalState = result.FinalState;
            var totalTime = DateTime.UtcNow - startTime;

            LogGraphExecutionCompleted(crew.Id, totalTime, result.TotalTransitions, result.Trace);

            var domainResults = finalState.DomainResults;
            var finalOutput = (domainResults.Count > 0 ? domainResults[^1] : null)?.Output ?? string.Empty;

            // A graph's nodes are not tasks, so there is no per-task moment to hook into
            // mid-run: the results are reported when the graph joins.
            var snapshots = await NotifyResultsAsync(domainResults, finalState).ConfigureAwait(false);

            // A task still failing after its retries fails the crew (GAP-03): the graph
            // reaching END used to be reported as a success whatever its tasks did.
            if (finalState.Outcome.HasFailures)
                LogGraphExecutionFailed(crew.Id, finalState.Outcome.Failures.Count, finalState.Outcome.Reason);

            return await finalState.Outcome.CompleteAsync(_hooks, new CrewRunSummary(
                crew.Id.ToString(), startTime, snapshots, domainResults, totalTime,
                BuildTokenMetadata(finalState), finalOutput)).ConfigureAwait(false);
        }
        catch (GraphCircuitBrokenException ex)
        {
            var totalTime = DateTime.UtcNow - startTime;
            LogGraphExecutionCircuitBroken(crew.Id, ex.Message);

            // The graph nodes mutate the state instance in place, so initialState carries
            // the tokens consumed up to the break — propagate them, they were paid for.
            var brokenSnapshots = await NotifyResultsAsync(initialState.DomainResults, initialState).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, brokenSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);

            // The break heads the error; the tasks that had already failed follow it. A task it
            // caught running — between two attempts — fails with it (GAP-21).
            await initialState.Outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            initialState.Outcome.RecordRunFailure($"Graph execution stopped by circuit breaker: {ex.Message}");
            return DomainCrewOutput.CreateFailure(
                error: initialState.Outcome.Reason,
                taskOutputs: initialState.DomainResults,
                executionTime: totalTime,
                metadata: BuildTokenMetadata(initialState));
        }
        }
        catch (OperationCanceledException ex)
        {
            // The terminal event goes out on every exit — setup included: an agent-less
            // crew or a graph that fails to compile is the everyday failure, and the
            // circuit breaker used to be the only failure this mode reported. The task the
            // cancellation caught is cancelled first (GAP-21).
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, [], CrewHookStatus.Canceled, "Execution was cancelled."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (GraphCircuitBrokenException)
        {
            // Already reported by the inner handler, which returned a failure output; a
            // rethrow only happens if that handler itself failed — let it surface as-is.
            throw;
        }
        catch (Exception ex)
        {
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, [], CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
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
        IReadOnlyList<DomainTaskOutput> results,
        CrewGraphState state)
    {
        var snapshots = new List<TaskExecutionSnapshot>(results.Count);
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            var taskId = result.TaskId?.Value.ToString() ?? string.Empty;
            var usage = index < state.ResultUsage.Count ? state.ResultUsage[index] : default;
            var snapshot = state.SkipReasons.TryGetValue(index, out var skipReason)
                ? CrewRunOutcome.SkippedSnapshot(result.TaskId!, "graph", skipReason)
                : new TaskExecutionSnapshot
                {
                    TaskId = taskId,
                    AgentRole = "graph",
                    Success = result.Success,
                    Duration = result.ExecutionTime,
                    CompletedAt = DateTimeOffset.UtcNow,
                    TokensUsed = usage.Tokens,
                    CacheHitTokens = usage.CacheHit,
                    CacheMissTokens = usage.CacheMiss,
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
                state.CompletionTokensUsed,
                state.CacheHitTokensUsed,
                state.CacheMissTokensUsed)
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
        _delegationProvider.UpdateExecutionContext(
            initialState.Context with { PreviousOutputs = initialState.ApplicationOutputs });

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

        var agent = await _agentSelector
            .ForTaskAsync(task, state.Agents, state.AgentIndex++, ct)
            .ConfigureAwait(false);

        // A task depending on one that did not succeed is skipped, as in Sequential (GAP-03).
        // A failed task is retried before the next task runs (see RouteNodeAsync), so by the
        // time a dependant is dequeued its dependency has either succeeded or given up.
        if (state.Outcome.BlockingDependency(task) is { } blockedBy)
        {
            var reason = await state.Outcome.RecordSkipAsync(task, agent.Role.Value, blockedBy).ConfigureAwait(false);
            LogTaskSkippedAfterDependency(task.Id, agent.Role.Value, blockedBy);
            var (skippedDomain, skippedApp) = CrewRunOutcome.SkippedOutputs(task.Id, agent.Id.ToString(), blockedBy);
            state.SkipReasons[state.DomainResults.Count] = reason;
            state.AddResultUsage(0, 0, 0);
            state.AddApplicationOutput(skippedApp);
            state.AddDomainResult(skippedDomain);
            return state;
        }

        // Completions are reported when the graph settles (NotifyResultsAsync); the start is
        // the one per-task moment this mode can announce live.
        await _hooks.TaskStartedAsync(
            CrewHookDispatcher.Started(taskId.Value.ToString(), agent.Role.Value), ct)
            .ConfigureAwait(false);
        // A retry goes on with the task its first attempt started: started once (GAP-21).
        await state.Outcome.RecordStartAsync(task, agent).ConfigureAwait(false);

        var executionResult = await RunTaskAsync(state, task, agent, ct).ConfigureAwait(false);

        AppendTaskOutputs(state, task, agent, executionResult);
        await EvaluateFailureForRetryAsync(state, task, agent, executionResult).ConfigureAwait(false);

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
        // Derived from the run's context, never rebuilt (GAP-30): a delegation from this task
        // derives its coworker's context from it in turn (GAP-21).
        var execContext = state.Context with { PreviousOutputs = state.ApplicationOutputs, CancellationToken = ct };
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
        var rawOutput = CrewRunOutcome.RawOutputOf(executionResult);
        state.TotalTokensUsed += executionResult.TokensUsed;
        state.PromptTokensUsed += executionResult.PromptTokens;
        state.CompletionTokensUsed += executionResult.CompletionTokens;
        state.CacheHitTokensUsed += executionResult.CacheHitTokens;
        state.CacheMissTokensUsed += executionResult.CacheMissTokens;

        // One usage entry per attempt, in DomainResults order: the snapshots below zip
        // the two lists, so a retry costs what ITS attempt cost — never a running total.
        state.AddResultUsage(executionResult.TokensUsed, executionResult.CacheHitTokens, executionResult.CacheMissTokens);

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
    /// Ends a task that succeeded; tracks the failure of one that did not and enqueues it for retry
    /// if cycles remain — it keeps running meanwhile — or fails it once it gives up.
    /// </summary>
    private async Task EvaluateFailureForRetryAsync(
        CrewGraphState state,
        Orkeon.Domain.Task.CrewTask task,
        DomainAgent agent,
        TaskResult executionResult)
    {
        var taskId = task.Id;
        if (executionResult.Success)
        {
            await state.Outcome.RecordSuccessAsync(task, state.DomainResults[^1]).ConfigureAwait(false);
            return;
        }

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
            await state.Outcome.RecordFailureAsync(task, agent.Role.Value, executionResult.Error ?? executionResult.LastError).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Route node body: puts a failed task with retries left back at the head of the queue,
    /// so it is retried before the next task runs. Retries used to wait until the queue was
    /// empty, and a task depending on the failed one ran in between on its failed output; now a
    /// dependant only ever sees a dependency that succeeded or gave up (GAP-03).
    /// </summary>
    private Task<CrewGraphState> RouteNodeAsync(CrewGraphState state, CancellationToken _)
    {
        if (state.FailedTaskIds.Count > 0)
        {
            LogPromotingFailedTasks(state.FailedTaskIds.Count);
            var remaining = state.PendingTaskIds.ToArray();
            state.PendingTaskIds.Clear();
            while (state.FailedTaskIds.Count > 0)
                state.PendingTaskIds.Enqueue(state.FailedTaskIds.Dequeue());
            foreach (var pending in remaining)
                state.PendingTaskIds.Enqueue(pending);
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

    /// <summary>
    /// The breaker of this run. An explicit <c>maxStateVisits</c> / <c>maxTransitions</c> in
    /// <c>graphConfig</c> wins; otherwise both are computed from the crew, since every attempt of
    /// a task is one visit of <c>execute_task</c> and one of <c>route</c>:
    /// <c>tasks × (1 + maxRetryCycles)</c> visits and twice that plus one transitions. The breaker
    /// keeps its job — stopping a real loop — without capping the size of a healthy crew. The
    /// duration stays the preset's (10 min under Strict), a cost choice
    /// <c>graphConfig.maxTotalDurationSeconds</c> overrides.
    /// </summary>
    private CircuitBreakerPolicy ResolvePolicy(DomainCrew crew, int taskCount, int maxRetryCycles)
    {
        if (crew.GraphConfig is null && CircuitPolicy is not null)
            return CircuitPolicy;

        var policy = CircuitBreakerPolicyFactory.ResolveGraph(crew.GraphConfig, CircuitBreakerPolicy.Strict);
        var explicitVisits = crew.GraphConfig?.MaxStateVisits;
        var explicitTransitions = crew.GraphConfig?.MaxTransitions;

        // An empty crew still visits execute_task once on its way to END. Clamped so the
        // transition bound never overflows.
        var visits = (int)Math.Min(
            (long)Math.Max(1, taskCount) * (1 + Math.Max(0, maxRetryCycles)),
            (int.MaxValue - 1) / 2);

        return policy with
        {
            MaxStateVisits = explicitVisits ?? visits,
            MaxTransitions = explicitTransitions ?? (2 * visits) + 1,
        };
    }

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


    #endregion

    #region Unsupported process types

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(
        DomainCrew crew, AgentId? managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Hierarchical execution is not supported by GraphProcessStrategy. " +
            "Use HierarchicalProcessStrategy instead.");
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(
        DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Graph execution of crew {CrewId} failed: {FailedTasks} task(s) did not succeed. {Reason}")]
    private partial void LogGraphExecutionFailed(CrewId crewId, int failedTasks, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {TaskId} ({AgentRole}) skipped: it depends on task {DependencyId}, which did not succeed")]
    private partial void LogTaskSkippedAfterDependency(TaskId taskId, string agentRole, TaskId dependencyId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrying {Count} failed task(s) before the next pending one")]
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

    /// <summary>Running total of cache-served prompt tokens (0 when unreported) (W-08).</summary>
    public long CacheHitTokensUsed { get; set; }

    /// <summary>Running total of cache-missed prompt tokens (0 when unreported) (W-08).</summary>
    public long CacheMissTokensUsed { get; set; }

    private readonly List<(int Tokens, long CacheHit, long CacheMiss)> _resultUsage = [];

    /// <summary>
    /// Per-ATTEMPT usage, aligned index-for-index with <see cref="DomainResults"/> — a
    /// retried task appends one result per attempt, so keying usage by task would stamp
    /// the cumulative cost on every attempt's snapshot and double-count the totals.
    /// </summary>
    public IReadOnlyList<(int Tokens, long CacheHit, long CacheMiss)> ResultUsage => _resultUsage;

    /// <summary>Appends one attempt's usage, in <see cref="DomainResults"/> order.</summary>
    public void AddResultUsage(int tokens, long cacheHit, long cacheMiss) =>
        _resultUsage.Add((tokens, cacheHit, cacheMiss));

    /// <summary>Round-robin agent index.</summary>
    public int AgentIndex { get; set; }

    /// <summary>Per-task retry counts (taskId string → count).</summary>
    public required Dictionary<string, int> RetryCounts { get; init; }

    /// <summary>Max retry cycles per failed task.</summary>
    public required int MaxRetryCycles { get; init; }

    /// <summary>
    /// The failures of this run and the tasks that did not succeed (GAP-03), and the lifecycle of the
    /// tasks it records (GAP-21).
    /// </summary>
    internal CrewRunOutcome Outcome { get; init; } = null!;

    /// <summary>The run's execution context, from which each task's is derived.</summary>
    internal SimpleExecutionContext Context { get; init; } = null!;

    /// <summary>Skip reasons, keyed by the index of the skipped task's entry in <see cref="DomainResults"/>.</summary>
    internal Dictionary<int, string> SkipReasons { get; } = [];
}
