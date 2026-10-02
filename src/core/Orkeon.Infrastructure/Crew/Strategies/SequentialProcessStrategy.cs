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
    private readonly TaskLifecycle _lifecycle;

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
        _lifecycle = dependencies.LifecycleFor(logger);
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
        var variables = inputVariables != null
            ? new Dictionary<string, string>(inputVariables)
            : [];
        var applicationOutputs = new List<ApplicationTaskOutput>();
        var run = new SequentialRun(
            new SimpleExecutionContext(crew.Id, variables, _memoryScope, applicationOutputs, cancellationToken),
            applicationOutputs,
            new CrewRunOutcome(_lifecycle));

        try
        {
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

            _delegationProvider.UpdateExecutionContext(run.Context);

            var taskIds = await CrewTaskSequencer.ResolveAsync(
                crew, plan, _taskRepository, _logger, cancellationToken).ConfigureAwait(false);
            var agentIndex = 0;

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

                var snapshot = run.Outcome.BlockingDependency(task) is { } blockedBy
                    ? await SkipBlockedTaskAsync(run, task, agent, blockedBy).ConfigureAwait(false)
                    : await RunTaskAsync(run, task, agent, cancellationToken).ConfigureAwait(false);

                if (_hooks.HasHook)
                {
                    run.TaskSnapshots.Add(snapshot);
                    await _hooks.TaskCompletedAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                }
            }

            var totalTime = DateTime.UtcNow - startTime;
            var finalOutput = run.DomainResults.LastOrDefault()?.Output ?? string.Empty;

            LogTotalTokensUsed(crew.Id, run.TokenTally.TotalTokens);

            var metadata = run.TokenTally
                .WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder())
                .Build();

            // A pipeline with a failed step is a failed pipeline: the crew used to report
            // success whatever its tasks did, so an empty deliverable went green all the way
            // to the runner's exit code (STUDIO-12 C5a). The reason names every failed task.
            if (run.Outcome.HasFailures)
                LogSequentialExecutionFailedForCrew(crew.Id, run.Outcome.Failures.Count, run.Outcome.Reason);
            else
                LogSequentialExecutionCompletedForCrew(crew.Id, totalTime);

            return await run.Outcome.CompleteAsync(
                _hooks, crew.Id.Value.ToString(), startedAt, run.TaskSnapshots,
                run.DomainResults, totalTime, metadata, finalOutput).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // The task the cancellation caught is cancelled, its agent failing it (GAP-21).
            await run.Outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.Value.ToString(), startedAt, run.TaskSnapshots, CrewHookStatus.Canceled,
                    "Crew execution was canceled (timeout or external cancellation)."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await run.Outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.Value.ToString(), startedAt, run.TaskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// What one sequential pass accumulates task after task: the execution context the next
    /// task reads (rebuilt after each one), the outputs in both shapes, the snapshots the hooks
    /// receive, the token tally, the failure reasons and the tasks that did not succeed.
    /// </summary>
    private sealed class SequentialRun
    {
        public SequentialRun(SimpleExecutionContext context, List<ApplicationTaskOutput> applicationOutputs, CrewRunOutcome outcome)
        {
            Context = context;
            ApplicationOutputs = applicationOutputs;
            Outcome = outcome;
        }

        public SimpleExecutionContext Context { get; set; }
        public List<ApplicationTaskOutput> ApplicationOutputs { get; }
        public List<DomainTaskOutput> DomainResults { get; } = [];
        public List<TaskExecutionSnapshot> TaskSnapshots { get; } = [];
        public TokenUsageTally TokenTally { get; } = new();

        /// <summary>
        /// The failures so far, and the tasks that did not succeed: a task depending on one of
        /// them is skipped in its turn, so a broken step never runs the rest of the chain on a
        /// context that says "Task failed: …" where the deliverable it needed should have been
        /// (LLM-11). The rule is shared by the six modes (GAP-03), and so is the lifecycle of the
        /// tasks and agents it records (GAP-21).
        /// </summary>
        public CrewRunOutcome Outcome { get; }
    }

    /// <summary>
    /// A task blocked by a dependency that did not succeed (LLM-11): recorded as skipped, never
    /// run — a failed output for the next tasks' context and the crew's result, and a snapshot
    /// marked <see cref="TaskExecutionSnapshot.Skipped"/> for the summary and the run events.
    /// No agent is asked anything, so nothing is started and no token is spent; the task is
    /// cancelled with the reason (GAP-21).
    /// </summary>
    private async System.Threading.Tasks.Task<TaskExecutionSnapshot> SkipBlockedTaskAsync(
        SequentialRun run, Orkeon.Domain.Task.CrewTask task, DomainAgent agent, TaskId blockedBy)
    {
        var reason = await run.Outcome.RecordSkipAsync(task, agent.Role.Value, blockedBy).ConfigureAwait(false);
        LogTaskSkippedAfterDependency(task.Id, agent.Role, blockedBy);

        var (domainOutput, applicationOutput) = CrewRunOutcome.SkippedOutputs(task.Id, agent.Id.ToString(), blockedBy);
        run.ApplicationOutputs.Add(applicationOutput);
        run.DomainResults.Add(domainOutput);
        run.Context = run.Context with { PreviousOutputs = run.ApplicationOutputs };
        _delegationProvider.UpdateExecutionContext(run.Context);

        return CrewRunOutcome.SkippedSnapshot(task.Id, agent.Role.Value, reason);
    }

    /// <summary>One task run by its agent, its outcome folded into the pass.</summary>
    private async System.Threading.Tasks.Task<TaskExecutionSnapshot> RunTaskAsync(
        SequentialRun run, Orkeon.Domain.Task.CrewTask task, DomainAgent agent, CancellationToken cancellationToken)
    {
        // The task is about to run: say so before asking the agent anything, so a
        // watcher shows it in progress instead of discovering it only once finished.
        await _hooks.TaskStartedAsync(
            CrewHookDispatcher.Started(task.Id.Value.ToString(), agent.Role.Value), CancellationToken.None)
            .ConfigureAwait(false);
        await run.Outcome.RecordStartAsync(task, agent).ConfigureAwait(false);

        Orkeon.Application.Interfaces.Services.TaskResult taskResult;
        TaskExecutionSnapshot snapshot;
        (run.Context, snapshot, taskResult) = await ExecuteSingleTaskAsync(
            task, agent, run.Context, run.ApplicationOutputs, run.DomainResults, cancellationToken)
            .ConfigureAwait(false);
        run.TokenTally.Record(taskResult);

        _delegationProvider.UpdateExecutionContext(run.Context);
        LogTaskCompletedSuccess(task.Id, snapshot.Success);

        if (taskResult.Success)
            await run.Outcome.RecordSuccessAsync(task, run.DomainResults[^1]).ConfigureAwait(false);
        else
            await run.Outcome.RecordFailureAsync(task, agent.Role.Value, taskResult.Error ?? taskResult.LastError).ConfigureAwait(false);

        return snapshot;
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

        var rawOutput = CrewRunOutcome.RawOutputOf(executionResult);

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

        // Derived, never rebuilt: a context's init settings survive from task to task (GAP-30).
        var updatedContext = context with { PreviousOutputs = applicationOutputs };

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
