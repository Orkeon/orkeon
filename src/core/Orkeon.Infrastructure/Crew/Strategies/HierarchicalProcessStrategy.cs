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
    private readonly IAgentExecutionService _executionService;
    private readonly IMemoryScope _memoryScope;
    private readonly CrewHookDispatcher _hooks;

    /// <summary>Initializes a new instance of <see cref="HierarchicalProcessStrategy"/>.</summary>
    /// <param name="taskRepository">The task repository.</param>
    /// <param name="agentRepository">The agent repository.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="managerAgent">The manager agent responsible for task delegation and review.</param>
    /// <param name="executionService">The agent execution service.</param>
    /// <param name="memoryScope">The memory scope.</param>
    /// <param name="hook">Optional execution hook notified as each task is finalised (BUS-03).</param>
    public HierarchicalProcessStrategy(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        ILogger<HierarchicalProcessStrategy> logger,
        IManagerAgent managerAgent,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope,
        ICrewExecutionHook? hook = null)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(managerAgent);
        _managerAgent = managerAgent;
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
        ArgumentNullException.ThrowIfNull(memoryScope);
        _memoryScope = memoryScope;
        _hooks = new CrewHookDispatcher(hook, logger);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, Orkeon.Domain.Crew.ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        // Not supported by this strategy
        throw new NotSupportedException(
            "Sequential execution is not supported by HierarchicalProcessStrategy. " +
            "Use SequentialProcessStrategy instead.");
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(managerAgentId);
        return ExecuteHierarchicalCoreAsync(crew, managerAgentId, inputVariables, cancellationToken);
    }

    private async Task<DomainCrewOutput> ExecuteHierarchicalCoreAsync(
        DomainCrew crew,
        AgentId managerAgentId,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingHierarchicalExecutionForCrew(crew.Id, managerAgentId);

        var startTime = DateTime.UtcNow;
        var results = new List<DomainTaskOutput>();
        var taskSnapshots = new List<TaskExecutionSnapshot>();

        // The terminal event goes out on EVERY exit — success, cancellation, failure — and
        // the barrier covers SETUP as well as the loop: a missing manager or an agent-less
        // crew is the everyday failure, and ending it without a terminal event left the
        // watcher's screen frozen on nothing at all.
        try
        {
            var managerAgent = await _agentRepository.GetByIdAsync(managerAgentId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Manager agent {managerAgentId} not found");

            var workerAgents = await GetWorkerAgentsAsync(crew, managerAgent).ConfigureAwait(false);

            if (workerAgents.Count == 0)
                throw new InvalidOperationException("No worker agents available for hierarchical execution");

            var variables = inputVariables != null
                ? new Dictionary<string, string>(inputVariables)
                : [];

            var applicationTaskOutputs = new List<ApplicationTaskOutput>();
            var context = new SimpleExecutionContext(
                crew.Id, variables, _memoryScope,
                applicationTaskOutputs, cancellationToken);

            // Token telemetry propagation (R10.8) — same metadata channel as Sequential.
            // Records every worker execution, including revision re-executions. The manager's
            // own assign/review LLM usage is not surfaced by IManagerAgent and stays unmetered.
            var tokenTally = new TokenUsageTally();

            foreach (var taskId in crew.Tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The loop is sequential, so the tally's delta around one task IS that
                // task's usage — revision re-executions included (W-08).
                var tokensBefore = tokenTally.TotalTokens;
                var cacheHitBefore = tokenTally.CacheHitTokens;
                var cacheMissBefore = tokenTally.CacheMissTokens;

                var (domainOutput, appOutput, updatedContext) = await ProcessSingleTaskAsync(
                    taskId, workerAgents, context, applicationTaskOutputs, tokenTally, cancellationToken).ConfigureAwait(false);

                if (domainOutput == null || appOutput == null)
                    continue;

                results.Add(domainOutput);
                applicationTaskOutputs.Add(appOutput);
                context = updatedContext!;

                var snapshot = new TaskExecutionSnapshot
                {
                    TaskId = taskId.Value.ToString(),
                    AgentRole = appOutput.AgentId ?? string.Empty,
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

            await _hooks.CrewCompletedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Completed),
                cancellationToken).ConfigureAwait(false);

            return BuildCrewOutput(results, workerAgents, managerAgent, crew, startTime, tokenTally);
        }
        catch (OperationCanceledException)
        {
            // The run's own token is cancelled; the dispatch must still go out.
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Canceled, "Execution was cancelled."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<List<DomainAgent>> GetWorkerAgentsAsync(DomainCrew crew, DomainAgent managerAgent)
    {
        var workerAgents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            if (agentId == managerAgent.Id)
                continue;

            var agent = await _agentRepository.GetByIdAsync(agentId).ConfigureAwait(false);
            if (agent != null)
                workerAgents.Add(agent);
        }
        return workerAgents;
    }

    private async Task<(DomainTaskOutput?, ApplicationTaskOutput?, SimpleExecutionContext?)> ProcessSingleTaskAsync(
        TaskId taskId,
        List<DomainAgent> workerAgents,
        SimpleExecutionContext context,
        List<ApplicationTaskOutput> applicationTaskOutputs,
        TokenUsageTally tokenTally,
        CancellationToken cancellationToken)
    {
        LogManagerProcessingTask(taskId);

        var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task == null)
        {
            LogTaskNotFoundSkipping(taskId);
            return (null, null, null);
        }

        var assignment = await _managerAgent.AssignTaskAsync(task, workerAgents, context).ConfigureAwait(false);
        LogManagerAssignedTaskToAgent(assignment.TaskId, assignment.AssignedAgent, assignment.Reason);

        var assignedAgent = workerAgents.FirstOrDefault(a => a.Id == assignment.AssignedAgent);
        if (assignedAgent == null)
        {
            LogAssignedAgentNotFound(assignment.AssignedAgent);
            return (null, null, null);
        }

        var (domainOutput, appOutput) = await ExecuteWithRevisionLoopAsync(
            assignedAgent, task, taskId, context, applicationTaskOutputs, tokenTally, cancellationToken).ConfigureAwait(false);

        var updatedContext = new SimpleExecutionContext(
            context.CrewId, context.Variables, context.Memory,
            applicationTaskOutputs, context.CancellationToken);

        return (domainOutput, appOutput, updatedContext);
    }

    private async Task<(DomainTaskOutput, ApplicationTaskOutput)> ExecuteWithRevisionLoopAsync(
        DomainAgent assignedAgent,
        CrewTask task,
        TaskId taskId,
        SimpleExecutionContext context,
        List<ApplicationTaskOutput> applicationTaskOutputs,
        TokenUsageTally tokenTally,
        CancellationToken cancellationToken)
    {
        var executionResult = await _executionService.ExecuteTaskAsync(
            assignedAgent, task, context, cancellationToken).ConfigureAwait(false);
        tokenTally.Record(executionResult);

        var appOutput = BuildApplicationTaskOutput(task, assignedAgent, executionResult);
        var domainOutput = BuildDomainTaskOutput(task, executionResult);

        const int MaxRevisions = 3;
        var revisionContext = new TaskRevisionContext(
            assignedAgent, task, taskId, context, applicationTaskOutputs, MaxRevisions);

        for (int revision = 0; revision < MaxRevisions; revision++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var approved = await _managerAgent.ReviewOutputAsync(appOutput, task).ConfigureAwait(false);
            if (approved)
                break;

            if (revision < MaxRevisions - 1)
            {
                (domainOutput, appOutput) = await ReExecuteTaskAsync(
                    revisionContext, revision, tokenTally, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                (domainOutput, appOutput) = MarkAsNeedsRevision(
                    taskId, MaxRevisions, domainOutput, appOutput);
            }
        }

        return (domainOutput, appOutput);
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

    private async Task<(DomainTaskOutput, ApplicationTaskOutput)> ReExecuteTaskAsync(
        TaskRevisionContext revisionContext,
        int revision,
        TokenUsageTally tokenTally,
        CancellationToken cancellationToken)
    {
        var (assignedAgent, task, taskId, context, applicationTaskOutputs, maxRevisions) = revisionContext;

        LogManagerRejectedOutputForTask(taskId, revision + 1, maxRevisions);

        var executionContext = new SimpleExecutionContext(
            context.CrewId,
            new Dictionary<string, string>(context.Variables)
            {
                ["revision_feedback"] = $"Previous output was rejected. Revision {revision + 1}/{maxRevisions}. Please improve."
            },
            context.Memory, applicationTaskOutputs, context.CancellationToken);

        var executionResult = await _executionService.ExecuteTaskAsync(
            assignedAgent, task, executionContext, cancellationToken).ConfigureAwait(false);
        tokenTally.Record(executionResult);

        return (BuildDomainTaskOutput(task, executionResult),
                BuildApplicationTaskOutput(task, assignedAgent, executionResult));
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
            Content: result.Output,
            CompletedAt: DateTime.UtcNow,
            Success: result.Success,
            ExecutionTime: result.ExecutionTime,
            ToolsUsed: result.ToolsUsed);
    }

    private static DomainTaskOutput BuildDomainTaskOutput(
        CrewTask task, TaskResult result)
    {
        return DomainTaskOutput.Create(
            rawOutput: result.Output,
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: result.Success,
            executionTime: result.ExecutionTime,
            structuredOutput: result.StructuredOutput);
    }

    private DomainCrewOutput BuildCrewOutput(
        List<DomainTaskOutput> results,
        List<DomainAgent> workerAgents,
        DomainAgent managerAgent,
        DomainCrew crew,
        DateTime startTime,
        TokenUsageTally tokenTally)
    {
        var finalOutput = string.Join("\n\n", results.Select(r => r.Output));
        var totalExecutionTime = DateTime.UtcNow - startTime;

        LogHierarchicalExecutionCompletedForCrew(crew.Id, totalExecutionTime);

        var metadata = tokenTally
            .WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder()
                .Add("process_type", "hierarchical")
                .Add("manager_agent", managerAgent.Id.ToString())
                .Add("worker_count", workerAgents.Count))
            .Build();

        return DomainCrewOutput.CreateSuccess(
            output: finalOutput,
            structuredOutput: null,
            taskOutputs: results,
            executionTime: totalExecutionTime,
            metadata: metadata);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, Orkeon.Domain.Crew.ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        // Not supported by this strategy
        throw new NotSupportedException(
            "Parallel execution is not supported by HierarchicalProcessStrategy. " +
            "Use ParallelProcessStrategy instead.");
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting hierarchical execution for crew {CrewId} with manager {ManagerId}")]
    private partial void LogStartingHierarchicalExecutionForCrew(CrewId crewId, AgentId managerId);

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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Hierarchical execution completed for crew {CrewId} in {Duration}")]
    private partial void LogHierarchicalExecutionCompletedForCrew(CrewId crewId, TimeSpan duration);

}
