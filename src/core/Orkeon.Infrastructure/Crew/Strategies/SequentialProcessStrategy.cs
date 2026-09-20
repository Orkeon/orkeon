using System.Collections.Immutable;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Orkeon.Infrastructure.Agent;
using Orkeon.Domain.Autonomous;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Sequential process strategy implementation.
/// Executes tasks one after another: in the plan's order when the crew was planned, otherwise
/// in the declared order sorted on the tasks' dependencies (<see cref="CrewTaskSequencer"/>).
/// </summary>
public sealed partial class SequentialProcessStrategy : IProcessStrategy
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentExecutionService _executionService;
    private readonly IMemoryScope _memoryScope;
    private readonly AgentDelegationToolsProvider _delegationProvider;
    private readonly ILogger<SequentialProcessStrategy> _logger;

    /// <summary>
    /// Best-effort hook dispatcher (BUS-03). Shared with the five other modes — this
    /// strategy used to carry its own private copy of the fault barrier, and the two
    /// implementations drifting apart is how the other modes shipped without one.
    /// </summary>
    private readonly CrewHookDispatcher _hooks;
    private readonly TaskAgentSelector _agentSelector;

    /// <summary>Initializes a new instance of <see cref="SequentialProcessStrategy"/>.</summary>
    /// <param name="dependencies">The collaborators shared by every crew strategy.</param>
    /// <param name="delegationProvider">The agent delegation tools provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="hook">Optional crew execution hook (e.g. <see cref="AutoSummaryWriter"/>). May be null.</param>
    /// <param name="agentSelector">Who runs a task that names no agent. Null means round-robin.</param>
    public SequentialProcessStrategy(
        CrewStrategyDependencies dependencies,
        AgentDelegationToolsProvider delegationProvider,
        ILogger<SequentialProcessStrategy> logger,
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
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteSequentialAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(plan);
        return ExecuteSequentialCoreAsync(crew, plan, inputVariables, cancellationToken);
    }

    private async System.Threading.Tasks.Task<DomainCrewOutput> ExecuteSequentialCoreAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingSequentialExecutionForCrew(crew.Id);

        var startedAt = DateTimeOffset.UtcNow;
        var startTime = startedAt.UtcDateTime;
        var domainResults = new List<Orkeon.Domain.Task.ValueObjects.TaskOutput>();
        var applicationOutputs = new List<ApplicationTaskOutput>();
        var taskSnapshots = new List<TaskExecutionSnapshot>();
        var tokenTally = new TokenUsageTally();

        try
        {
            var variables = inputVariables != null
                ? new Dictionary<string, string>(inputVariables)
                : [];

            var context = new SimpleExecutionContext(
                crew.Id,
                variables,
                _memoryScope,
                applicationOutputs,
                cancellationToken);

            // Setup inside the barrier — an agent-less crew is the everyday failure, and it
            // has to produce a terminal event like any other exit.
            var agents = await LoadAgentsAsync(crew).ConfigureAwait(false);

            // Register agent entities and add delegation tools
            foreach (var agent in agents)
            {
                _delegationProvider.RegisterAgentEntity(agent);
                _delegationProvider.AddDelegationToolsToAgent(agent);
            }

            if (agents.Count == 0 && crew.Tasks.Count > 0)
                throw new InvalidOperationException("No agents available for sequential execution");

            _delegationProvider.UpdateExecutionContext(context);

            var taskIds = await CrewTaskSequencer.ResolveAsync(
                crew, plan, _taskRepository, _logger, cancellationToken).ConfigureAwait(false);
            var agentIndex = 0;
            var failures = new List<string>();
            // Every task that failed or was skipped: a task depending on one of them is skipped
            // in its turn, so a broken step never runs the rest of the chain on a context that
            // says "Task failed: …" where the deliverable it needed should have been (LLM-11).
            var notSucceeded = new HashSet<TaskId>();

            foreach (var taskId in taskIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogExecutingTask(taskId);

                var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
                if (task == null)
                {
                    LogTaskNotFoundSkipping(taskId);
                    continue;
                }

                var agent = await _agentSelector
                    .ForTaskAsync(task, agents, agentIndex++, cancellationToken)
                    .ConfigureAwait(false);

                if (BlockingDependency(task, notSucceeded) is { } blockedBy)
                {
                    TaskExecutionSnapshot skippedSnapshot;
                    (context, skippedSnapshot) = RecordSkippedTask(
                        task, agent, blockedBy, context, applicationOutputs, domainResults);
                    notSucceeded.Add(taskId);
                    failures.Add(skippedSnapshot.SkipReason!);
                    _delegationProvider.UpdateExecutionContext(context);

                    if (_hooks.HasHook)
                    {
                        taskSnapshots.Add(skippedSnapshot);
                        await _hooks.TaskCompletedAsync(skippedSnapshot, CancellationToken.None).ConfigureAwait(false);
                    }
                    continue;
                }

                // The task is about to run: say so before asking the agent anything, so a
                // watcher shows it in progress instead of discovering it only once finished.
                await _hooks.TaskStartedAsync(
                    CrewHookDispatcher.Started(task.Id.Value.ToString(), agent.Role.Value), CancellationToken.None)
                    .ConfigureAwait(false);

                Orkeon.Application.Interfaces.Services.TaskResult taskResult;
                (context, var taskSnapshot, taskResult) = await ExecuteSingleTaskAsync(
                    task, agent, context, applicationOutputs, domainResults, cancellationToken)
                    .ConfigureAwait(false);
                tokenTally.Record(taskResult);

                _delegationProvider.UpdateExecutionContext(context);
                LogTaskCompletedSuccess(taskId, taskSnapshot.Success);

                if (!taskResult.Success)
                {
                    notSucceeded.Add(taskId);
                    failures.Add(
                        $"Task {taskId} ({agent.Role}) failed: {taskResult.Error ?? taskResult.LastError ?? "unknown error"}");
                }

                if (_hooks.HasHook)
                {
                    taskSnapshots.Add(taskSnapshot);
                    await _hooks.TaskCompletedAsync(taskSnapshot, CancellationToken.None).ConfigureAwait(false);
                }
            }

            var totalTime = DateTime.UtcNow - startTime;
            var finalOutput = domainResults.LastOrDefault()?.Output ?? string.Empty;

            LogTotalTokensUsed(crew.Id, tokenTally.TotalTokens);

            var metadata = tokenTally
                .WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder())
                .Build();

            // A pipeline with a failed step is a failed pipeline: the crew used to report
            // success whatever its tasks did, so an empty deliverable went green all the way
            // to the runner's exit code (STUDIO-12 C5a). The reason names every failed task.
            if (failures.Count > 0)
            {
                var reason = string.Join("; ", failures);
                LogSequentialExecutionFailedForCrew(crew.Id, failures.Count, reason);

                await _hooks.CrewFailedAsync(
                    CrewHookDispatcher.Snapshot(
                        crew.Id.Value.ToString(), startedAt, taskSnapshots, CrewHookStatus.Failed, reason),
                    null, CancellationToken.None).ConfigureAwait(false);

                return DomainCrewOutput.CreateFailure(
                    error: reason,
                    taskOutputs: domainResults,
                    executionTime: totalTime,
                    metadata: metadata,
                    output: finalOutput);
            }

            LogSequentialExecutionCompletedForCrew(crew.Id, totalTime);

            await _hooks.CrewCompletedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.Value.ToString(), startedAt, taskSnapshots, CrewHookStatus.Completed),
                CancellationToken.None).ConfigureAwait(false);

            return DomainCrewOutput.CreateSuccess(
                output: finalOutput,
                structuredOutput: null,
                taskOutputs: domainResults,
                executionTime: totalTime,
                metadata: metadata);
        }
        catch (OperationCanceledException) when (_hooks.HasHook)
        {
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.Value.ToString(), startedAt, taskSnapshots, CrewHookStatus.Canceled,
                    "Crew execution was canceled (timeout or external cancellation)."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (_hooks.HasHook)
        {
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.Value.ToString(), startedAt, taskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async System.Threading.Tasks.Task<(SimpleExecutionContext Context, TaskExecutionSnapshot Snapshot, Orkeon.Application.Interfaces.Services.TaskResult Result)> ExecuteSingleTaskAsync(
        Orkeon.Domain.Task.CrewTask task,
        DomainAgent agent,
        SimpleExecutionContext context,
        List<ApplicationTaskOutput> applicationOutputs,
        List<DomainTaskOutput> domainResults,
        CancellationToken cancellationToken)
    {
        var executionResult = await _executionService.ExecuteTaskAsync(
            agent, task, context, cancellationToken).ConfigureAwait(false);

        if (executionResult.ExitReason != AgentExitReason.Completed)
        {
            LogAgentExitedWithReason(
                agent.Role,
                executionResult.ExitReason.ToString(),
                executionResult.IterationsUsed,
                executionResult.LastError ?? executionResult.Error ?? "(none)");
        }

        var rawOutput = GetRawOutput(executionResult);

        var appOutput = new ApplicationTaskOutput(
            TaskId: task.Id.Value.ToString(),
            AgentId: agent.Id.ToString(),
            Content: rawOutput,
            CompletedAt: DateTime.UtcNow,
            Success: executionResult.Success,
            ExecutionTime: executionResult.ExecutionTime,
            ToolsUsed: executionResult.ToolsUsed);
        applicationOutputs.Add(appOutput);

        domainResults.Add(DomainTaskOutput.Create(
            rawOutput: rawOutput,
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: executionResult.Success,
            executionTime: executionResult.ExecutionTime,
            structuredOutput: executionResult.StructuredOutput,
            agentId: agent.Id.ToString()));

        var updatedContext = new SimpleExecutionContext(
            context.CrewId,
            context.Variables,
            context.Memory,
            applicationOutputs,
            context.CancellationToken);

        var snapshot = new TaskExecutionSnapshot
        {
            TaskId = task.Id.Value.ToString(),
            AgentRole = agent.Role?.ToString() ?? string.Empty,
            Success = executionResult.Success,
            Duration = executionResult.ExecutionTime,
            CompletedAt = DateTimeOffset.UtcNow,
            ToolCallCount = executionResult.ToolsUsed?.Count ?? 0,
            TokensUsed = executionResult.TokensUsed,
            CacheHitTokens = executionResult.CacheHitTokens,
            CacheMissTokens = executionResult.CacheMissTokens,
            UnknownFqns = executionResult.UnknownFqns,
            RewrittenFqns = executionResult.RewrittenFqns,
            AmbiguousFqns = executionResult.AmbiguousFqns,
        };

        return (updatedContext, snapshot, executionResult);
    }

    private async System.Threading.Tasks.Task<List<DomainAgent>> LoadAgentsAsync(DomainCrew crew)
    {
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId).ConfigureAwait(false);
            if (agent != null) agents.Add(agent);
        }
        return agents;
    }

    /// <summary>
    /// The first declared dependency of <paramref name="task"/> that failed or was skipped,
    /// or null when the task may run. Only the direct dependencies are read: a skipped task
    /// joins <paramref name="notSucceeded"/> itself, so the transitive closure follows.
    /// </summary>
    private static TaskId? BlockingDependency(Orkeon.Domain.Task.CrewTask task, HashSet<TaskId> notSucceeded)
    {
        foreach (var dependency in task.Dependencies)
        {
            if (notSucceeded.Contains(dependency))
                return dependency;
        }

        return null;
    }

    /// <summary>
    /// Records a task that does not run because <paramref name="blockedBy"/> did not succeed
    /// (LLM-11): a failed output for the next tasks' context and the crew's result, and a
    /// snapshot marked <see cref="TaskExecutionSnapshot.Skipped"/> for the summary and the
    /// run events. No agent is asked anything, so nothing is started and no token is spent.
    /// </summary>
    private (SimpleExecutionContext Context, TaskExecutionSnapshot Snapshot) RecordSkippedTask(
        Orkeon.Domain.Task.CrewTask task,
        DomainAgent agent,
        TaskId blockedBy,
        SimpleExecutionContext context,
        List<ApplicationTaskOutput> applicationOutputs,
        List<DomainTaskOutput> domainResults)
    {
        var reason = $"Task {task.Id} ({agent.Role}) skipped: it depends on task {blockedBy}, which did not succeed";
        LogTaskSkippedAfterDependency(task.Id, agent.Role, blockedBy);

        var rawOutput = $"Task skipped: dependency {blockedBy} did not succeed";
        applicationOutputs.Add(new ApplicationTaskOutput(
            TaskId: task.Id.Value.ToString(),
            AgentId: agent.Id.ToString(),
            Content: rawOutput,
            CompletedAt: DateTime.UtcNow,
            Success: false,
            ExecutionTime: TimeSpan.Zero));

        domainResults.Add(DomainTaskOutput.Create(
            rawOutput: rawOutput,
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: false,
            executionTime: TimeSpan.Zero,
            structuredOutput: null,
            agentId: agent.Id.ToString()));

        var updatedContext = new SimpleExecutionContext(
            context.CrewId,
            context.Variables,
            context.Memory,
            applicationOutputs,
            context.CancellationToken);

        var snapshot = new TaskExecutionSnapshot
        {
            TaskId = task.Id.Value.ToString(),
            AgentRole = agent.Role?.ToString() ?? string.Empty,
            Success = false,
            Duration = TimeSpan.Zero,
            CompletedAt = DateTimeOffset.UtcNow,
            Skipped = true,
            SkipReason = reason,
        };

        return (updatedContext, snapshot);
    }

    private static string GetRawOutput(Orkeon.Application.Interfaces.Services.TaskResult result)
    {
        if (!string.IsNullOrEmpty(result.Output))
            return result.Output;
        if (result.Success)
            return "(no output)";
        return $"Task failed: {result.Error ?? "unknown error"}";
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Hierarchical execution is not supported by SequentialProcessStrategy. " +
            "Use HierarchicalProcessStrategy instead.");
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, DomainExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Parallel execution is not supported by SequentialProcessStrategy. " +
            "Use ParallelProcessStrategy instead.");
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting sequential execution for crew {CrewId}")]
    private partial void LogStartingSequentialExecutionForCrew(CrewId crewId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Executing task {TaskId}")]
    private partial void LogExecutingTask(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} not found, skipping")]
    private partial void LogTaskNotFoundSkipping(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId} completed, success: {Success}")]
    private partial void LogTaskCompletedSuccess(TaskId taskId, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Sequential execution completed for crew {CrewId} in {Duration}")]
    private partial void LogSequentialExecutionCompletedForCrew(CrewId crewId, TimeSpan duration);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Sequential execution of crew {CrewId} failed: {FailedTasks} task(s) did not succeed. {Reason}")]
    private partial void LogSequentialExecutionFailedForCrew(CrewId crewId, int failedTasks, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Total tokens used for crew {CrewId}: {TokensUsed}")]
    private partial void LogTotalTokensUsed(CrewId crewId, int tokensUsed);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Agent [{AgentRole}] exited with reason {ExitReason} after {IterationsUsed} iterations. Last error: {LastError}")]
    private partial void LogAgentExitedWithReason(object agentRole, string exitReason, int iterationsUsed, string lastError);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} ({AgentRole}) skipped: it depends on task {DependencyId}, which did not succeed")]
    private partial void LogTaskSkippedAfterDependency(TaskId taskId, object agentRole, TaskId dependencyId);

}
