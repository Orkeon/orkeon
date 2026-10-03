using Microsoft.Extensions.Logging;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Orkeon.Domain.Common;
using Orkeon.Domain.Autonomous;
// Resolve ambiguous references
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Hierarchical process strategy implementation.
/// Manager agent delegates tasks to other agents using LLM-based decisions.
/// </summary>
public sealed partial class HierarchicalProcessStrategy : IProcessStrategy
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<HierarchicalProcessStrategy> _logger;
    private readonly IManagerAgent _managerAgent;
    private readonly ManagerLlmResolver _managerLlm;
    private readonly IAgentExecutionService _executionService;
    private readonly IMemoryScope _memoryScope;
    private readonly IMemoryCoordinator _memoryCoordinator;
    private readonly CrewHookDispatcher _hooks;
    private readonly TaskLifecycle _lifecycle;

    /// <summary>The role a skipped task reports: the manager was never asked to assign it.</summary>
    private const string UnassignedRole = "unassigned";

    /// <summary>The role the manager answers to when the crew's manager LLM manages without an agent.</summary>
    private const string ManagerRole = "manager";

    /// <summary>Initializes a new instance of <see cref="HierarchicalProcessStrategy"/>.</summary>
    /// <param name="taskRepository">The task repository.</param>
    /// <param name="agentRepository">The agent repository.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="managerAgent">The manager agent responsible for task delegation and review.</param>
    /// <param name="managerLlm">
    /// Resolves, once per run, the LLM the crew gives its manager — <c>Crew.ManagerLlm</c>, else the
    /// manager agent's profile and model, else the host's default profile (GAP-19).
    /// </param>
    /// <param name="executionService">The agent execution service.</param>
    /// <param name="memoryScope">The memory scope.</param>
    /// <param name="memoryCoordinator">
    /// Stores the output the manager accepted in the crew's memory, once, under the agent that
    /// wrote it: the attempts run without storing (GAP-30).
    /// </param>
    /// <param name="hook">Optional execution hook notified as each task is finalised (BUS-03).</param>
    /// <param name="domainEvents">
    /// Delivers the events of the tasks and agents as the run moves them (GAP-21); the container
    /// always provides one. Without it they are moved and saved, their events left queued.
    /// </param>
#pragma warning disable S107 // DI constructor: the four collaborators of every strategy, the manager and its LLM, the memory, the hook and the dispatcher
    public HierarchicalProcessStrategy(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        ILogger<HierarchicalProcessStrategy> logger,
        IManagerAgent managerAgent,
        ManagerLlmResolver managerLlm,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope,
        IMemoryCoordinator memoryCoordinator,
        ICrewExecutionHook? hook = null,
        Orkeon.Domain.SharedKernel.Events.IDomainEventDispatcher? domainEvents = null)
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(managerAgent);
        _managerAgent = managerAgent;
        ArgumentNullException.ThrowIfNull(managerLlm);
        _managerLlm = managerLlm;
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
        ArgumentNullException.ThrowIfNull(memoryScope);
        _memoryScope = memoryScope;
        ArgumentNullException.ThrowIfNull(memoryCoordinator);
        _memoryCoordinator = memoryCoordinator;
        _hooks = new CrewHookDispatcher(hook, logger);
        _lifecycle = new TaskLifecycle(taskRepository, agentRepository, domainEvents, logger);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        // Not supported by this strategy
        throw new NotSupportedException(
            "Sequential execution is not supported by HierarchicalProcessStrategy. " +
            "Use SequentialProcessStrategy instead.");
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId? managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return ExecuteHierarchicalCoreAsync(crew, managerAgentId, inputVariables, cancellationToken);
    }

    private async Task<DomainCrewOutput> ExecuteHierarchicalCoreAsync(
        DomainCrew crew,
        AgentId? managerAgentId,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingHierarchicalExecutionForCrew(crew.Id);

        var startTime = DateTime.UtcNow;
        var results = new List<DomainTaskOutput>();
        var taskSnapshots = new List<TaskExecutionSnapshot>();
        var outcome = new CrewRunOutcome(_lifecycle, cancellationToken);

        // The terminal event goes out on EVERY exit — success, cancellation, failure — and
        // the barrier covers SETUP as well as the loop: a missing manager or an agent-less
        // crew is the everyday failure, and ending it without a terminal event left the
        // watcher's screen frozen on nothing at all.
        try
        {
            // The manager is the crew's manager agent — removed from the workers — or, when C# gave
            // the crew a manager LLM and no agent, that LLM alone: every agent then works (GAP-19).
            var managerAgent = managerAgentId is null
                ? null
                : await _agentRepository.GetByIdAsync(managerAgentId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Manager agent {managerAgentId} not found");
            if (managerAgent is null && crew.ManagerLlm is null)
                throw new InvalidOperationException(
                    "Hierarchical process requires a manager agent or a manager LLM (CrewBuilder.WithManagerLlm).");

            var workerAgents = await GetWorkerAgentsAsync(crew, managerAgent?.Id).ConfigureAwait(false);

            if (workerAgents.Count == 0)
                throw new InvalidOperationException("No worker agents available for hierarchical execution");

            // The LLM the crew gives its manager, once for the run: never the default in silence.
            var managerLlm = _managerLlm.Resolve(crew, managerAgent);
            var managerRole = managerAgent?.Role.Value ?? ManagerRole;
            LogManagerLlm(crew.Id, managerRole, managerLlm.Name);

            // The manager hands out the work: its assignments and reviews are metered under its
            // role (STUDIO-42). Each worker's execution opens its own scope and names itself.
            using var managerUsageScope = LlmUsageScope.Begin(agentId: managerRole);

            var variables = inputVariables != null
                ? new Dictionary<string, string>(inputVariables)
                : [];

            var applicationTaskOutputs = new List<ApplicationTaskOutput>();
            var context = new SimpleExecutionContext(
                crew.Id, variables, _memoryScope,
                applicationTaskOutputs, cancellationToken);

            // Token telemetry propagation (R10.8) — same metadata channel as Sequential.
            // Records every worker execution, including revision re-executions. The manager's
            // own assign/review usage belongs to no task, so no task's figure carries it; the
            // run's token meter counts it through the metered provider (STUDIO-42).
            var tokenTally = new TokenUsageTally();

            // The manager hands the tasks out one after another, so the order is the
            // sequential one: the declared order sorted on the dependencies (STUDIO-12 C2).
            var taskIds = await CrewTaskSequencer.ResolveAsync(
                crew, _taskRepository, _logger, cancellationToken).ConfigureAwait(false);

            foreach (var taskId in taskIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
                if (task == null)
                {
                    LogTaskNotFoundSkipping(taskId);
                    continue;
                }

                // A task depending on one that did not succeed is skipped, as in Sequential
                // (GAP-03): the manager is not asked to assign work that would run on a
                // "Task failed: …" where its input should be.
                if (outcome.BlockingDependency(task) is { } blockedBy)
                {
                    var skipReason = await outcome.RecordSkipAsync(task, UnassignedRole, blockedBy).ConfigureAwait(false);
                    LogTaskSkippedAfterDependency(task.Id, blockedBy);
                    var (skippedDomain, skippedApp) = CrewRunOutcome.SkippedOutputs(task.Id, agentId: null, blockedBy);
                    results.Add(skippedDomain);
                    applicationTaskOutputs.Add(skippedApp);
                    context = context with { PreviousOutputs = applicationTaskOutputs };
                    var skipped = CrewRunOutcome.SkippedSnapshot(task.Id, UnassignedRole, skipReason);
                    taskSnapshots.Add(skipped);
                    await _hooks.TaskCompletedAsync(skipped, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // The loop is sequential, so the tally's delta around one task IS that
                // task's usage — revision re-executions included (W-08).
                var tokensBefore = tokenTally.TotalTokens;
                var cacheHitBefore = tokenTally.CacheHitTokens;
                var cacheMissBefore = tokenTally.CacheMissTokens;

                var processed = await ProcessSingleTaskAsync(
                    task, workerAgents, managerLlm, context, applicationTaskOutputs, tokenTally, outcome, cancellationToken).ConfigureAwait(false);

                if (processed.Assignee is null)
                {
                    // The manager named an agent the crew does not carry: the task never ran,
                    // and a crew with a task that never ran did not complete.
                    await outcome.RecordFailureAsync(task, managerRole,
                        $"the manager assigned it to agent {processed.AssignedAgentId}, who is not a worker of this crew").ConfigureAwait(false);
                    continue;
                }

                var (domainOutput, appOutput) = (processed.Domain!, processed.Application!);
                results.Add(domainOutput);
                applicationTaskOutputs.Add(appOutput);
                context = processed.Context!;

                // The agent the manager assigned ends the task it started — its revisions were
                // part of its work on it (GAP-21).
                if (domainOutput.Success)
                    await outcome.RecordSuccessAsync(task, domainOutput).ConfigureAwait(false);
                else
                    await outcome.RecordFailureAsync(task, processed.Assignee.Role.Value, processed.Error).ConfigureAwait(false);

                var snapshot = new TaskExecutionSnapshot
                {
                    TaskId = taskId.Value.ToString(),
                    // The role, not the agent's GUID the output carries: the start event names
                    // the role and a watcher pairs the two by it (STUDIO-17).
                    AgentRole = processed.Assignee.Role.Value,
                    Success = domainOutput.Success,
                    Duration = domainOutput.ExecutionTime,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ToolCallCount = appOutput.ToolsUsed?.Count ?? 0,
                    TokensUsed = tokenTally.TotalTokens - tokensBefore,
                    CacheHitTokens = tokenTally.CacheHitTokens - cacheHitBefore,
                    CacheMissTokens = tokenTally.CacheMissTokens - cacheMissBefore,
                };
                taskSnapshots.Add(snapshot);
                await _hooks.TaskCompletedAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }

            return await BuildCrewOutputAsync(
                outcome, results, taskSnapshots, workerAgents, managerAgent, managerLlm, crew, startTime, tokenTally)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // The run's own token is cancelled; the dispatch must still go out, and the task it
            // caught is cancelled (GAP-21).
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Canceled, "Execution was cancelled."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<List<DomainAgent>> GetWorkerAgentsAsync(DomainCrew crew, AgentId? managerAgentId)
    {
        var workerAgents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            if (agentId == managerAgentId)
                continue;

            var agent = await _agentRepository.GetByIdAsync(agentId).ConfigureAwait(false);
            if (agent != null)
                workerAgents.Add(agent);
        }
        return workerAgents;
    }

    /// <summary>
    /// One task handed out by the manager: who it was assigned to (null when the manager named
    /// an agent the crew does not carry), its outputs, the context the next task reads, and
    /// the cause of its failure when it failed.
    /// </summary>
    private sealed record ProcessedTask(
        DomainAgent? Assignee,
        AgentId AssignedAgentId,
        DomainTaskOutput? Domain = null,
        ApplicationTaskOutput? Application = null,
        SimpleExecutionContext? Context = null,
        string? Error = null);

    private async Task<ProcessedTask> ProcessSingleTaskAsync(
        CrewTask task,
        List<DomainAgent> workerAgents,
        ManagerLlm managerLlm,
        SimpleExecutionContext context,
        List<ApplicationTaskOutput> applicationTaskOutputs,
        TokenUsageTally tokenTally,
        CrewRunOutcome outcome,
        CancellationToken cancellationToken)
    {
        LogManagerProcessingTask(task.Id);

        var assignment = await _managerAgent.AssignTaskAsync(task, workerAgents, context, managerLlm).ConfigureAwait(false);
        LogManagerAssignedTaskToAgent(assignment.TaskId, assignment.AssignedAgent, assignment.Reason);

        var assignedAgent = workerAgents.FirstOrDefault(a => a.Id == assignment.AssignedAgent);
        if (assignedAgent == null)
        {
            LogAssignedAgentNotFound(assignment.AssignedAgent);
            return new ProcessedTask(null, assignment.AssignedAgent);
        }

        await _hooks.TaskStartedAsync(
            CrewHookDispatcher.Started(task.Id.Value.ToString(), assignedAgent.Role.Value), cancellationToken)
            .ConfigureAwait(false);
        // The agent the manager assigned starts the task — re-assigned to it when the crew declared
        // another (GAP-21).
        await outcome.RecordStartAsync(task, assignedAgent).ConfigureAwait(false);

        var (domainOutput, appOutput, error) = await ExecuteWithRevisionLoopAsync(
            assignedAgent, task, task.Id, managerLlm, context, applicationTaskOutputs, tokenTally, cancellationToken).ConfigureAwait(false);

        // Derived, never rebuilt: a context's init settings survive from task to task (GAP-30).
        var updatedContext = context with { PreviousOutputs = applicationTaskOutputs };

        return new ProcessedTask(assignedAgent, assignment.AssignedAgent, domainOutput, appOutput, updatedContext, error);
    }

#pragma warning disable S107 // the task, who runs it and who reviews it, its context and the run's tallies
    private async Task<(DomainTaskOutput Domain, ApplicationTaskOutput Application, string? Error)> ExecuteWithRevisionLoopAsync(
        DomainAgent assignedAgent,
        CrewTask task,
        TaskId taskId,
        ManagerLlm managerLlm,
        SimpleExecutionContext context,
        List<ApplicationTaskOutput> applicationTaskOutputs,
        TokenUsageTally tokenTally,
        CancellationToken cancellationToken)
#pragma warning restore S107
    {
        // An attempt is not the task's result until the manager accepts it: every attempt runs
        // without storing (it still recalls — it answers the task), and the accepted output is
        // stored once, below (GAP-30). Derived with `with`, never rebuilt, so the context's init
        // settings travel with it.
        var attemptContext = context with { StoreResultInMemory = false };

        var executionResult = await _executionService.ExecuteTaskAsync(
            assignedAgent, task, attemptContext, cancellationToken).ConfigureAwait(false);
        tokenTally.Record(executionResult);

        var appOutput = BuildApplicationTaskOutput(task, assignedAgent, executionResult);
        var domainOutput = BuildDomainTaskOutput(task, executionResult);
        var error = executionResult.Error ?? executionResult.LastError;

        const int MaxRevisions = 3;
        var revisionContext = new TaskRevisionContext(
            assignedAgent, task, taskId, attemptContext, applicationTaskOutputs, MaxRevisions);

        var accepted = false;
        for (int revision = 0; revision < MaxRevisions; revision++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var approved = await _managerAgent.ReviewOutputAsync(appOutput, task, managerLlm, cancellationToken).ConfigureAwait(false);
            if (approved)
            {
                accepted = true;
                break;
            }

            if (revision < MaxRevisions - 1)
            {
                (domainOutput, appOutput, error, executionResult) = await ReExecuteTaskAsync(
                    revisionContext, revision, tokenTally, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                (domainOutput, appOutput) = MarkAsNeedsRevision(
                    taskId, MaxRevisions, domainOutput, appOutput);
                error = $"the manager rejected its output after {MaxRevisions} revisions";
            }
        }

        // What the crew remembers: the output the manager accepted — a review that errors counts
        // as an acceptance — once, under the agent that wrote it; never a rejected attempt, never
        // a failed one. A crew without memory stores nothing; a store that fails is a warning.
        if (accepted && executionResult.Success && !string.IsNullOrEmpty(executionResult.Output))
        {
            await _memoryCoordinator.StoreTaskResultAsync(
                assignedAgent, task, executionResult.Output, context, cancellationToken).ConfigureAwait(false);
        }

        return (domainOutput, appOutput, error);
    }

    /// <summary>
    /// Groups the parameters that remain stable across the revision loop of a single task.
    /// </summary>
    private sealed record TaskRevisionContext(
        DomainAgent AssignedAgent,
        CrewTask Task,
        TaskId TaskId,
        SimpleExecutionContext Context,
        List<ApplicationTaskOutput> ApplicationTaskOutputs,
        int MaxRevisions);

    private async Task<(DomainTaskOutput, ApplicationTaskOutput, string?, TaskResult)> ReExecuteTaskAsync(
        TaskRevisionContext revisionContext,
        int revision,
        TokenUsageTally tokenTally,
        CancellationToken cancellationToken)
    {
        var (assignedAgent, task, taskId, context, applicationTaskOutputs, maxRevisions) = revisionContext;

        LogManagerRejectedOutputForTask(taskId, revision + 1, maxRevisions);

        // Derived from the attempt context: it stores nothing, like the first attempt.
        var executionContext = context with
        {
            Variables = new Dictionary<string, string>(context.Variables)
            {
                ["revision_feedback"] = $"Previous output was rejected. Revision {revision + 1}/{maxRevisions}. Please improve."
            },
            PreviousOutputs = applicationTaskOutputs,
        };

        var executionResult = await _executionService.ExecuteTaskAsync(
            assignedAgent, task, executionContext, cancellationToken).ConfigureAwait(false);
        tokenTally.Record(executionResult);

        return (BuildDomainTaskOutput(task, executionResult),
                BuildApplicationTaskOutput(task, assignedAgent, executionResult),
                executionResult.Error ?? executionResult.LastError,
                executionResult);
    }

    private (DomainTaskOutput, ApplicationTaskOutput) MarkAsNeedsRevision(
        TaskId taskId,
        int maxRevisions,
        DomainTaskOutput domainOutput,
        ApplicationTaskOutput appOutput)
    {
        LogManagerRejectedOutputForTask2(taskId, maxRevisions);

        var updatedDomain = DomainTaskOutput.Create(
            rawOutput: $"[NEEDS REVISION] {domainOutput.Output}",
            format: domainOutput.Format,
            formattedOutput: domainOutput.FormattedOutput,
            taskId: domainOutput.TaskId,
            success: false,
            executionTime: domainOutput.ExecutionTime,
            structuredOutput: domainOutput.StructuredOutput);

        var updatedApp = new ApplicationTaskOutput(
            TaskId: appOutput.TaskId,
            Content: $"[NEEDS REVISION] {appOutput.Content}",
            AgentId: appOutput.AgentId,
            CompletedAt: appOutput.CompletedAt,
            Success: false,
            ExecutionTime: appOutput.ExecutionTime,
            ToolsUsed: appOutput.ToolsUsed);

        return (updatedDomain, updatedApp);
    }

    private static ApplicationTaskOutput BuildApplicationTaskOutput(
        CrewTask task, DomainAgent agent, TaskResult result)
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

    private static DomainTaskOutput BuildDomainTaskOutput(
        CrewTask task, TaskResult result)
    {
        return DomainTaskOutput.Create(
            rawOutput: CrewRunOutcome.RawOutputOf(result),
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: result.Success,
            executionTime: result.ExecutionTime,
            structuredOutput: result.StructuredOutput);
    }

#pragma warning disable S107 // the run's outcome and outputs, who managed it and on what, and its tallies
    private async Task<DomainCrewOutput> BuildCrewOutputAsync(
        CrewRunOutcome outcome,
        List<DomainTaskOutput> results,
        List<TaskExecutionSnapshot> taskSnapshots,
        List<DomainAgent> workerAgents,
        DomainAgent? managerAgent,
        ManagerLlm managerLlm,
        DomainCrew crew,
        DateTime startTime,
        TokenUsageTally tokenTally)
#pragma warning restore S107
    {
        var finalOutput = string.Join("\n\n", results.Select(r => r.Output));
        var totalExecutionTime = DateTime.UtcNow - startTime;

        if (outcome.HasFailures)
            LogHierarchicalExecutionFailedForCrew(crew.Id, outcome.Failures.Count, outcome.Reason);
        else
            LogHierarchicalExecutionCompletedForCrew(crew.Id, totalExecutionTime);

        var metadataBuilder = Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder()
            .Add("process_type", "hierarchical")
            .Add("manager_llm", managerLlm.Name)
            .Add("worker_count", workerAgents.Count);
        if (managerAgent is not null)
            metadataBuilder = metadataBuilder.Add("manager_agent", managerAgent.Id.ToString());
        var metadata = tokenTally.WriteTo(metadataBuilder).Build();

        // A task the manager kept rejecting, or whose worker failed, fails the crew (GAP-03):
        // "[NEEDS REVISION]" used to sit in a crew reported as completed.
        return await outcome.CompleteAsync(
            _hooks, crew.Id.ToString(), startTime, taskSnapshots,
            results, totalExecutionTime, metadata, finalOutput).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        // Not supported by this strategy
        throw new NotSupportedException(
            "Parallel execution is not supported by HierarchicalProcessStrategy. " +
            "Use ParallelProcessStrategy instead.");
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting hierarchical execution for crew {CrewId}")]
    private partial void LogStartingHierarchicalExecutionForCrew(CrewId crewId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Crew {CrewId}: the manager ({Manager}) assigns and reviews on {ManagerLlm}")]
    private partial void LogManagerLlm(CrewId crewId, string manager, string managerLlm);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Manager processing task {TaskId}")]
    private partial void LogManagerProcessingTask(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} not found, skipping")]
    private partial void LogTaskNotFoundSkipping(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Manager assigned task {TaskId} to agent {AgentId}: {Reason}")]
    private partial void LogManagerAssignedTaskToAgent(TaskId taskId, AgentId agentId, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Assigned agent {AgentId} not found")]
    private partial void LogAssignedAgentNotFound(AgentId agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Manager rejected output for task {TaskId} (revision {Revision}/{Max}), re-executing")]
    private partial void LogManagerRejectedOutputForTask(TaskId taskId, int revision, int max);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Manager rejected output for task {TaskId} after {Max} revisions, marking as needs revision")]
    private partial void LogManagerRejectedOutputForTask2(TaskId taskId, int max);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Hierarchical execution of crew {CrewId} failed: {FailedTasks} task(s) did not succeed. {Reason}")]
    private partial void LogHierarchicalExecutionFailedForCrew(CrewId crewId, int failedTasks, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} skipped: it depends on task {DependencyId}, which did not succeed")]
    private partial void LogTaskSkippedAfterDependency(TaskId taskId, TaskId dependencyId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Hierarchical execution completed for crew {CrewId} in {Duration}")]
    private partial void LogHierarchicalExecutionCompletedForCrew(CrewId crewId, TimeSpan duration);

}
