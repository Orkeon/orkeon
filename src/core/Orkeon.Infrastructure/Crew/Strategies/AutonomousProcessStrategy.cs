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
    private readonly TaskLifecycle _lifecycle;

    /// <summary>The role every autonomous task reports: the manager picks its agent per task.</summary>
    private const string AutonomousRole = "autonomous";

    /// <summary>Initializes a new instance of <see cref="AutonomousProcessStrategy"/>.</summary>
    /// <param name="dependencies">The collaborators shared by every crew strategy.</param>
    /// <param name="channel">The A2A channel agents register on.</param>
    /// <param name="managerAgent">Scores which agent claims an unassigned task.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="hook">Optional crew execution hook. May be null (BUS-03).</param>
    public AutonomousProcessStrategy(
        CrewStrategyDependencies dependencies,
        IAgentChannel channel,
        IManagerAgent managerAgent,
        ILogger<AutonomousProcessStrategy> logger,
        ICrewExecutionHook? hook = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _taskRepository = dependencies.TaskRepository;
        _agentRepository = dependencies.AgentRepository;
        _executionService = dependencies.ExecutionService;
        _memoryScope = dependencies.MemoryScope;
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
        ArgumentNullException.ThrowIfNull(managerAgent);
        _managerAgent = managerAgent;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _hooks = new CrewHookDispatcher(hook, logger);
        _lifecycle = dependencies.LifecycleFor(logger);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use ExecuteAutonomousAsync for autonomous orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use ExecuteAutonomousAsync for autonomous orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
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

        var applicationOutputs = new List<ApplicationTaskOutput>();
        var domainResults = new List<TaskOutput>();
        var agents = new List<DomainAgent>();
        var tokenTally = new TokenUsageTally();
        var taskSnapshots = new List<TaskExecutionSnapshot>();
        var outcome = new CrewRunOutcome(_lifecycle);
        var takeovers = new System.Collections.Concurrent.ConcurrentDictionary<Guid, Takeover>();
        IReadOnlyList<TaskId> taskIds = [];
        var nextTask = 0;
        List<IDisposable> registrations = [];

        try
        {
            // Setup inside the barrier: an agent-less crew is the everyday failure, and it
            // has to produce a terminal event like any other exit.
            agents = await LoadAgentsAsync(crew).ConfigureAwait(false);

            if (agents.Count == 0)
                throw new InvalidOperationException("No agents available for autonomous execution.");

            // Token telemetry propagation (R10.8) — same metadata channel as Sequential.
            // Thread-safe: A2A channel handlers record delegated executions concurrently.
            // (The tally is created above so the compiler can see it assigned on the
            // BudgetExhausted fall-through; it records nothing until agents register.)

            // Register all agents on the channel
            registrations = RegisterAgentsOnChannel(agents, budget, tokenTally, takeovers);

            var variables = inputVariables != null
                ? new Dictionary<string, string>(inputVariables)
                : [];

            // The crew's tasks are handed out one after another, so the order is the
            // sequential one: the declared order sorted on the dependencies (STUDIO-12 C2).
            taskIds = await CrewTaskSequencer.ResolveAsync(
                crew, _taskRepository, _logger, cancellationToken).ConfigureAwait(false);

            for (; nextTask < taskIds.Count; nextTask++)
            {
                var taskId = taskIds[nextTask];
                cancellationToken.ThrowIfCancellationRequested();
                budget.AssertWallTime();

                var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
                if (task is null)
                {
                    LogTaskNotFound(taskId);
                    continue;
                }

                // A task depending on one that did not succeed is skipped, as in Sequential
                // (GAP-03): no agent claims it, and it spends nothing from the budget.
                if (outcome.BlockingDependency(task) is { } blockedBy)
                {
                    var skipReason = await outcome.RecordSkipAsync(task, AutonomousRole, blockedBy).ConfigureAwait(false);
                    LogTaskSkippedAfterDependency(task.Id, blockedBy);
                    var (skippedDomain, skippedApp) = CrewRunOutcome.SkippedOutputs(task.Id, agentId: null, blockedBy);
                    domainResults.Add(skippedDomain);
                    applicationOutputs.Add(skippedApp);
                    taskSnapshots.Add(CrewRunOutcome.SkippedSnapshot(task.Id, AutonomousRole, skipReason));
                    continue;
                }

                var executed = await ExecuteTaskAutonomouslyAsync(
                    new AutonomousTaskContext(task, agents, budget, crew.Id, variables, applicationOutputs, tokenTally)
                    {
                        Outcome = outcome,
                        Takeovers = takeovers,
                    },
                    cancellationToken)
                    .ConfigureAwait(false);

                domainResults.Add(executed.Domain);
                applicationOutputs.Add(executed.App);
                taskSnapshots.Add(new TaskExecutionSnapshot
                {
                    TaskId = task.Id.Value.ToString(),
                    AgentRole = AutonomousRole,
                    Success = executed.Domain.Success,
                    Duration = executed.Domain.ExecutionTime,
                    CompletedAt = DateTimeOffset.UtcNow,
                    // Deliberately no per-task tool count: the budget counter is crew-wide, and
                    // stamping it on every task over-reported by a factor of N. The real totals
                    // travel in the crew metadata (budget_tool_calls).
                });

                // The agent that ran it last ends the task: the one that claimed it, or the peer it
                // was handed to (GAP-21).
                if (executed.Domain.Success)
                    await outcome.RecordSuccessAsync(task, executed.Domain).ConfigureAwait(false);
                else
                    await outcome.RecordFailureAsync(task, executed.AgentRole, executed.Error).ConfigureAwait(false);

                // Guard snapshot allocation: only materialize when the log level is enabled
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var snapshot = budget.ToSnapshot();
                    LogTaskCompleted(taskId, executed.Domain.Success, snapshot);
                }

                if (executed.Exhausted is { } exhausted)
                {
                    // The budget is crew-wide: once one dimension ran out inside a task, no
                    // later task can run either.
                    await RecordBudgetExhaustionAsync(outcome, crew.Id, exhausted, taskIds.Skip(nextTask + 1)).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (BudgetExhaustedException ex)
        {
            // Between two tasks: the wall time ran out before the next one could start.
            await RecordBudgetExhaustionAsync(outcome, crew.Id, ex, taskIds.Skip(nextTask)).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // The terminal event goes out on every exit — hooks used to live only on the
            // success path (BuildOutputAsync), so a Ctrl+C froze the watcher mid-run. The task
            // the cancellation caught is cancelled first (GAP-21).
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, [], CrewHookStatus.Canceled, "Execution was cancelled."),
                null, CancellationToken.None).ConfigureAwait(false);
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
        finally
        {
            // Unregister all agents
            foreach (var reg in registrations)
                reg.Dispose();
        }

        return await BuildOutputAsync(
            outcome, domainResults, taskSnapshots, agents, crew, startTime, budget, tokenTally).ConfigureAwait(false);
    }

    // ── Private methods ──────────────────────────────────────────────────

    /// <summary>
    /// An exhausted budget fails the crew, naming the dimension and every task it never reached
    /// (GAP-03). It used to be swallowed: the crew reported success while the hook said Canceled.
    /// The tasks it never reached are cancelled, saying why (GAP-21).
    /// </summary>
    private async Task RecordBudgetExhaustionAsync(
        CrewRunOutcome outcome, CrewId crewId, BudgetExhaustedException ex, IEnumerable<TaskId> notExecuted)
    {
        LogBudgetExhausted(crewId, ex.Dimension, ex.Message);
        outcome.RecordRunFailure($"Execution budget exhausted: {ex.Dimension} ({ex.Message})");
        await outcome.RecordNotExecutedAsync(
            [.. notExecuted], $"the execution budget ran out ({ex.Dimension}) before it").ConfigureAwait(false);
    }

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
        TokenUsageTally tokenTally,
        System.Collections.Concurrent.ConcurrentDictionary<Guid, Takeover> takeovers)
    {
        var registrations = new List<IDisposable>();
        foreach (var agent in agents)
        {
            var capturedAgent = agent;
            var reg = _channel.RegisterHandler(agent.Id, async (request, ct) =>
            {
                // Handle delegation requests from peers: a task of this run handed over after its
                // agent failed it. A request naming no such task is refused.
                if (request.Intent == "delegate")
                {
                    if (!takeovers.TryGetValue(request.CorrelationId, out var takeover))
                    {
                        return AgentChannelResponse.Fail(
                            request.CorrelationId, capturedAgent.Id,
                            "No task of this run was handed over under this request.");
                    }

                    try
                    {
                        // Derive a child budget from the parent. This represents the
                        // reduced allowance for the delegated execution. We propagate
                        // it through the execution context so downstream components
                        // can honour it, and we use it to short-circuit early if the
                        // wall-time is already exhausted.
                        var childBudget = budget.CreateChildBudget();
                        childBudget.AssertWallTime();

                        // The peer takes the task itself over, in the context of the attempt that
                        // failed, derived and never rebuilt (GAP-21): the crew's id and memory scope,
                        // its inputs and the outputs so far. It recalls the crew's memory like any
                        // execution that answers a task, and its output, when it succeeds, is the
                        // task's result — stored once, under the peer that produced it.
                        var context = takeover.Context with
                        {
                            Variables = new Dictionary<string, string>(takeover.Context.Variables)
                            {
                                ["delegation_context"] = request.Payload,
                                [ChildBudgetSnapshotVariable] = FormatBudgetSnapshot(childBudget.ToSnapshot()),
                            },
                            CancellationToken = ct,
                        };

                        var result = await _executionService.ExecuteTaskAsync(
                            capturedAgent, takeover.Task, context, ct).ConfigureAwait(false);

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
    /// telemetry (R10.8); <see cref="Outcome"/> records the task's lifecycle and
    /// <see cref="Takeovers"/> the tasks this run hands to a peer (GAP-21).
    /// </summary>
    private sealed record AutonomousTaskContext(
        CrewTask Task,
        List<DomainAgent> Agents,
        AgentExecutionBudget Budget,
        CrewId CrewId,
        Dictionary<string, string> Variables,
        List<ApplicationTaskOutput> PreviousOutputs,
        TokenUsageTally TokenTally)
    {
        public required CrewRunOutcome Outcome { get; init; }

        public required System.Collections.Concurrent.ConcurrentDictionary<Guid, Takeover> Takeovers { get; init; }
    }

    /// <summary>
    /// A task this run handed to a peer after its agent failed it: the task itself, and the context
    /// of the attempt that failed, which the peer's derives from.
    /// </summary>
    private sealed record Takeover(CrewTask Task, SimpleExecutionContext Context);

    /// <summary>
    /// What one task produced: its outputs, the role that answers for it, the cause of its
    /// failure when it failed, and the budget exhaustion that interrupted it, if any.
    /// </summary>
    private sealed record ExecutedTask(
        TaskOutput Domain,
        ApplicationTaskOutput App,
        string AgentRole,
        string? Error = null,
        BudgetExhaustedException? Exhausted = null);

    private async Task<ExecutedTask> ExecuteTaskAutonomouslyAsync(
        AutonomousTaskContext taskContext,
        CancellationToken cancellationToken)
    {
        var (task, agents, budget, crewId, variables, previousOutputs, tokenTally) = taskContext;
        var outcome = taskContext.Outcome;

        // Let the LLM-based manager pick the best agent
        var context = new SimpleExecutionContext(
            crewId, variables, _memoryScope, previousOutputs, cancellationToken);

        var assignment = await _managerAgent.AssignTaskAsync(task, agents, context).ConfigureAwait(false);
        var agent = agents.FirstOrDefault(a => a.Id == assignment.AssignedAgent)
                    ?? agents[0]; // fallback to first

        LogAgentClaimedTask(agent.Id, task.Id, assignment.Reason);

        // Completions are reported when the run settles; the claim is the live moment. The agent
        // that claims the task starts it (GAP-21).
        await _hooks.TaskStartedAsync(
            CrewHookDispatcher.Started(task.Id.Value.ToString(), agent.Role.Value), cancellationToken)
            .ConfigureAwait(false);
        await outcome.RecordStartAsync(task, agent).ConfigureAwait(false);

        try
        {
            budget.RecordToolCall(); // count the LLM call for assignment

            var result = await _executionService.ExecuteTaskAsync(
                agent, task, context, cancellationToken).ConfigureAwait(false);

            // The direct attempt consumed tokens even when it fails and delegation
            // takes over below — record it before any branching.
            tokenTally.Record(result);

            var error = result.Error ?? result.LastError;

            // A failed task is handed to a peer when the agent may delegate and the budget
            // allows it. With no peer in the crew, the failure stands — it used to throw out
            // of the strategy instead.
            if (!result.Success && agent.AllowDelegation && budget.CurrentDelegationDepth < budget.MaxDelegationDepth)
            {
                if (agents.Exists(a => a.Id != agent.Id))
                    return await AttemptDelegationAsync(taskContext, agent, context, error).ConfigureAwait(false);

                error = $"{error ?? "unknown error"}; no peer to delegate it to";
            }

            return new ExecutedTask(
                BuildDomainTaskOutput(task, result),
                BuildAppTaskOutput(task, agent, result),
                agent.Role.Value,
                error);
        }
        catch (BudgetExhaustedException ex)
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

            return new ExecutedTask(
                partialOutput, partialApp, agent.Role.Value,
                $"interrupted: execution budget exhausted ({ex.Dimension})", ex);
        }
    }

    private async Task<ExecutedTask> AttemptDelegationAsync(
        AutonomousTaskContext taskContext,
        DomainAgent originalAgent,
        SimpleExecutionContext failedContext,
        string? failure)
    {
        var (task, agents, budget, _, _, _, _) = taskContext;
        budget.RecordDelegation();

        // Delegate to the first other agent (the caller checked there is one)
        var delegateAgent = agents.First(a => a.Id != originalAgent.Id);
        var request = AgentChannelRequest.Create(
            originalAgent.Id,
            delegateAgent.Id,
            "delegate",
            task.Description.Value);

        LogDelegation(originalAgent.Id, delegateAgent.Id, task.Id, budget.CurrentDelegationDepth);

        // The peer takes the task over: the agent that failed it says so, the task goes on under
        // the peer, which runs it in the failed attempt's context (GAP-21).
        await taskContext.Outcome.RecordHandOverAsync(task, delegateAgent, failure ?? "unknown error").ConfigureAwait(false);
        taskContext.Takeovers[request.CorrelationId] = new Takeover(task, failedContext);

        AgentChannelResponse response;
        try
        {
            response = await _channel.RequestAsync(
                request,
                timeout: TimeSpan.FromMinutes(2))
                .ConfigureAwait(false);
        }
        finally
        {
            taskContext.Takeovers.TryRemove(request.CorrelationId, out _);
        }

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

        return new ExecutedTask(
            output, appOutput, delegateAgent.Role.Value,
            response.Success ? null : $"delegated to {delegateAgent.Role}, who failed: {response.Error}");
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
            rawOutput: CrewRunOutcome.RawOutputOf(result),
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
            Content: CrewRunOutcome.RawOutputOf(result),
            CompletedAt: DateTime.UtcNow,
            Success: result.Success,
            ExecutionTime: result.ExecutionTime,
            ToolsUsed: result.ToolsUsed);
    }

    private async Task<DomainCrewOutput> BuildOutputAsync(
        CrewRunOutcome outcome,
        List<TaskOutput> results,
        List<TaskExecutionSnapshot> taskSnapshots,
        List<DomainAgent> agents,
        DomainCrew crew,
        DateTime startTime,
        AgentExecutionBudget budget,
        TokenUsageTally tokenTally)
    {
        var finalOutput = string.Join("\n\n", results.Select(r => r.Output));
        var totalTime = DateTime.UtcNow - startTime;
        var snapshot = budget.ToSnapshot();

        if (outcome.HasFailures)
            LogAutonomousExecutionFailed(crew.Id, outcome.Reason);
        else
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
        // into, so the outcome is reported when the run settles.
        foreach (var taskSnapshot in taskSnapshots)
            await _hooks.TaskCompletedAsync(taskSnapshot).ConfigureAwait(false);

        // An exhausted budget is a failure, reported as Failed with the dimension — Canceled
        // stays reserved for an actual cancellation (GAP-03).
        return await outcome.CompleteAsync(
            _hooks, crew.Id.ToString(), startTime, taskSnapshots, results, totalTime, metadata, finalOutput)
            .ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Autonomous execution of crew {CrewId} failed: {Reason}")]
    private partial void LogAutonomousExecutionFailed(CrewId crewId, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Task {TaskId} skipped: it depends on task {DependencyId}, which did not succeed")]
    private partial void LogTaskSkippedAfterDependency(TaskId taskId, TaskId dependencyId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Autonomous execution completed for crew {CrewId} in {Duration} (tool calls: {ToolCalls}, delegation depth: {Depth})")]
    private partial void LogAutonomousExecutionCompleted(CrewId crewId, TimeSpan duration, int toolCalls, int depth);
}
