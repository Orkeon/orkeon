using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Orkeon.Domain.Autonomous;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Parallel process strategy: everything that can run at once, does.
/// <para>
/// Tasks are grouped into dependency waves. A wave holds every task whose declared
/// <c>dependencies:</c> are already satisfied; they run concurrently, and the next wave starts
/// when they are all done, reading their outputs. A crew declaring no dependency is a single
/// wave — one flat fan-out, as before.
/// </para>
/// <para>
/// This mode used to ignore <c>dependencies:</c> outright. Twenty-three of the thirty shipped
/// <c>process: parallel</c> examples declare them, and in every one of those a final synthesis
/// task started at the same instant as the tasks it consumes, ran against an empty context and
/// reported success on the nothing it had. The documentation said to use Sequential or Graph
/// instead; twenty-three example authors disagreed with the documentation, and they were
/// describing the mode people actually want.
/// </para>
/// </summary>
public sealed partial class ParallelProcessStrategy : IProcessStrategy
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentExecutionService _executionService;
    private readonly IMemoryScope _memoryScope;
    private readonly CrewHookDispatcher _hooks;
    private readonly TaskAgentSelector _agentSelector;
    private readonly ILogger<ParallelProcessStrategy> _logger;
    private readonly TaskLifecycle _lifecycle;

    /// <summary>Initializes a new instance of <see cref="ParallelProcessStrategy"/>.</summary>
    /// <param name="taskRepository">The task repository.</param>
    /// <param name="agentRepository">The agent repository.</param>
    /// <param name="executionService">The agent execution service.</param>
    /// <param name="memoryScope">The memory scope.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="agentSelector">Who runs a task that names no agent. Null means round-robin.</param>
    /// <param name="hook">Optional crew execution hook. May be null (BUS-03).</param>
    /// <param name="domainEvents">
    /// Delivers the events of the tasks and agents as the run moves them (GAP-21); the container
    /// always provides one. Without it they are moved and saved, their events left queued.
    /// </param>
#pragma warning disable S107 // DI constructor: the four collaborators of every strategy, the logger, and three optional services
    public ParallelProcessStrategy(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope,
        ILogger<ParallelProcessStrategy> logger,
        ICrewExecutionHook? hook = null,
        TaskAgentSelector? agentSelector = null,
        Orkeon.Domain.SharedKernel.Events.IDomainEventDispatcher? domainEvents = null)
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
        ArgumentNullException.ThrowIfNull(memoryScope);
        _memoryScope = memoryScope;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _hooks = new CrewHookDispatcher(hook, logger);
        _agentSelector = agentSelector ?? TaskAgentSelector.RoundRobin;
        _lifecycle = new TaskLifecycle(taskRepository, agentRepository, domainEvents, logger);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Sequential execution is not supported by ParallelProcessStrategy. " +
            "Use SequentialProcessStrategy instead.");
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId? managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Hierarchical execution is not supported by ParallelProcessStrategy. " +
            "Use HierarchicalProcessStrategy instead.");
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return ExecuteParallelCoreAsync(crew, inputVariables, cancellationToken);
    }

    private async System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelCoreAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingParallelExecutionForCrew(crew.Id);

        var startTime = DateTime.UtcNow;
        var variables = inputVariables != null
            ? new Dictionary<string, string>(inputVariables)
            : [];

        var executionTasks = new List<System.Threading.Tasks.Task<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput, string? error)>>();

        // The barrier covers setup AND the fan-out loop, not only the WhenAll: a cancellation
        // firing mid-fan-out used to escape with tasks 1..n-1 already launched — no terminal
        // event, and orphans still emitting task.completed after the strategy had returned.
        var run = new ParallelRun(new CrewRunOutcome(_lifecycle, cancellationToken));
        var outcome = run.Outcome;
        try
        {
        // Setup stays inside the barrier: an agent-less crew is the everyday failure, and it
        // has to produce a terminal event like any other exit.
        var agents = await LoadAgentsAsync(crew, cancellationToken).ConfigureAwait(false);
        var tasks = await LoadTasksAsync(crew, cancellationToken).ConfigureAwait(false);

        var taskIndex = 0;
        var runContext = new SimpleExecutionContext(crew.Id, variables, _memoryScope, [], cancellationToken);

        // Waves, not one flat fan-out. A crew declaring `dependencies:` used to have them
        // ignored here: every task started at once, so a synthesis task ran against an empty
        // context while the tasks it consumes were still running, and reported success on the
        // nothing it had. Tasks with no unmet dependency go together; the next wave starts
        // when they are done, with their outputs in context. A crew declaring no dependency
        // is one wave — exactly the previous behaviour.
        foreach (var wave in DependencyWaves(tasks))
        {
            executionTasks.Clear();

            // Snapshot what the previous waves produced: every task in this wave reads the
            // same context, and the list must not be mutated while they run.
            var previousOutputs = run.CompletedOutputs.ToList();

            var launched = new List<(DomainTask Task, DomainAgent Agent)>();
            foreach (var task in wave)
            {
                // The agent the crew declared, round-robin only when it declared none — the same
                // choice Sequential and Graph make. This mode used to take loop order alone, so a
                // YAML `agent:` was silently ignored in parallel mode and nowhere else.
                var agent = await _agentSelector
                    .ForTaskAsync(task, agents, taskIndex++, cancellationToken)
                    .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // A task depending on one that did not succeed is skipped, as in Sequential
                // (GAP-03): it used to run in the next wave with "Task failed: …" as its input.
                if (outcome.BlockingDependency(task) is { } blockedBy)
                {
                    await SkipBlockedTaskAsync(run, task, agent, blockedBy, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Derived from the run's context, never rebuilt (GAP-30): every task of the wave
                // reads what the previous waves produced.
                var context = runContext with { PreviousOutputs = previousOutputs };

                var capturedTask = task;
                var capturedAgent = agent;

                // Started as it is launched, so the start goes out before its agent runs (GAP-21).
                // One agent given two tasks of the wave runs both at once.
                await outcome.RecordStartAsync(task, agent).ConfigureAwait(false);
                launched.Add((task, agent));
                executionTasks.Add(System.Threading.Tasks.Task.Run(async () => await ExecuteWaveTaskAsync(
                    capturedAgent, capturedTask, context, run.TokenTally, run.TaskSnapshots, cancellationToken)
                    .ConfigureAwait(false)));
            }

            // Wait for this wave. One faulted task means WhenAll throws — the terminal event
            // must still go out, or a watcher sees a run frozen at its last completed sibling.
            var waveResults = await System.Threading.Tasks.Task.WhenAll(executionTasks).ConfigureAwait(false);
            await RecordWaveAsync(run, launched, waveResults).ConfigureAwait(false);
        }
        }
        catch (OperationCanceledException ex)
        {
            // Let the already-launched tasks settle before the terminal event: they observe
            // the same token, and a task.completed emitted AFTER the terminal event would
            // read as a run speaking from beyond its own grave. The wave's tasks are then
            // cancelled, their agents failing them (GAP-21).
            await SettleAsync(executionTasks).ConfigureAwait(false);
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, run.TaskSnapshots, CrewHookStatus.Canceled, "Execution was cancelled."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await SettleAsync(executionTasks).ConfigureAwait(false);
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, run.TaskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var domainResults = run.Results.Select(r => r.domainOutput).ToList();
        var totalTime = DateTime.UtcNow - startTime;

        // Aggregate: combine all outputs
        var allOutputs = string.Join("\n\n", domainResults.Select(r => r.Output));

        if (outcome.HasFailures)
            LogParallelExecutionFailedForCrew(crew.Id, outcome.Failures.Count, outcome.Reason);
        else
            LogParallelExecutionCompletedForCrew(crew.Id, totalTime);

        return await outcome.CompleteAsync(_hooks, new CrewRunSummary(
            crew.Id.ToString(), startTime, run.TaskSnapshots, domainResults, totalTime,
            run.TokenTally.WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder()).Build(),
            allOutputs)).ConfigureAwait(false);
    }

    /// <summary>
    /// What one parallel run accumulates across its waves: the outcome (failures, lifecycle), the
    /// outputs in the order the waves recorded them, the outputs the next wave reads, the snapshots
    /// the hooks heard — a launched task adds its own from its own flow — and the token tally,
    /// thread-safe for the same reason (R10.8).
    /// </summary>
    private sealed class ParallelRun
    {
        public ParallelRun(CrewRunOutcome outcome)
        {
            Outcome = outcome;
        }

        public CrewRunOutcome Outcome { get; }
        public List<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput)> Results { get; } = [];
        public List<ApplicationTaskOutput> CompletedOutputs { get; } = [];
        public System.Collections.Concurrent.ConcurrentBag<TaskExecutionSnapshot> TaskSnapshots { get; } = [];
        public TokenUsageTally TokenTally { get; } = new();
    }

    /// <summary>
    /// A task blocked by a dependency that did not succeed: recorded as skipped and never run — a
    /// failed output for the next waves' context and the crew's result, a skipped snapshot for the hooks.
    /// </summary>
    private async System.Threading.Tasks.Task SkipBlockedTaskAsync(
        ParallelRun run, DomainTask task, DomainAgent agent, TaskId blockedBy, CancellationToken cancellationToken)
    {
        var skipReason = await run.Outcome.RecordSkipAsync(task, agent.Role.Value, blockedBy).ConfigureAwait(false);
        LogTaskSkippedAfterDependency(task.Id, agent.Role.Value, blockedBy);
        var skipped = CrewRunOutcome.SkippedOutputs(task.Id, agent.Id.ToString(), blockedBy);
        run.Results.Add((skipped.Domain, skipped.Application));
        run.CompletedOutputs.Add(skipped.Application);
        var skippedSnapshot = CrewRunOutcome.SkippedSnapshot(task.Id, agent.Role.Value, skipReason);
        run.TaskSnapshots.Add(skippedSnapshot);
        await _hooks.TaskCompletedAsync(skippedSnapshot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A wave that has joined: its failures are recorded in declaration order, so the crew's error
    /// reads the same whichever task finished first, and its outputs join the next wave's context.
    /// </summary>
    private static async System.Threading.Tasks.Task RecordWaveAsync(
        ParallelRun run,
        List<(DomainTask Task, DomainAgent Agent)> launched,
        (DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput, string? error)[] waveResults)
    {
        for (var i = 0; i < waveResults.Length; i++)
        {
            var (domainOutput, appOutput, error) = waveResults[i];
            if (domainOutput.Success)
                await run.Outcome.RecordSuccessAsync(launched[i].Task, domainOutput).ConfigureAwait(false);
            else
                await run.Outcome.RecordFailureAsync(launched[i].Task, launched[i].Agent.Role.Value, error).ConfigureAwait(false);
            run.Results.Add((domainOutput, appOutput));
        }

        run.CompletedOutputs.AddRange(waveResults.Select(r => r.appOutput));
    }

    /// <summary>
    /// The crew's agents, in declaration order. An id the repository cannot resolve is dropped,
    /// and a crew left with none has nothing to fan out — it says so instead of running an empty
    /// wave and reporting success on it.
    /// </summary>
    private async System.Threading.Tasks.Task<List<DomainAgent>> LoadAgentsAsync(
        DomainCrew crew,
        CancellationToken cancellationToken)
    {
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId, cancellationToken).ConfigureAwait(false);
            if (agent != null) agents.Add(agent);
        }

        if (agents.Count == 0)
            throw new InvalidOperationException("No agents available for parallel execution");

        return agents;
    }

    /// <summary>
    /// The tasks to run: the crew's own, in the order it declares them — with or without a plan, which
    /// never decides the order, the round-robin of unassigned tasks nor the order of the assembled
    /// output (GAP-31). A task id nothing resolves is logged and skipped rather than guessed at.
    /// </summary>
    private async System.Threading.Tasks.Task<List<DomainTask>> LoadTasksAsync(
        DomainCrew crew,
        CancellationToken cancellationToken)
    {
        var tasks = new List<DomainTask>();
        foreach (var taskId in crew.Tasks)
        {
            var loaded = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (loaded == null)
            {
                LogTaskNotFoundSkipping(taskId);
                continue;
            }

            tasks.Add(loaded);
        }

        return tasks;
    }

    /// <summary>
    /// One task of a wave, from the pool thread the fan-out launched it on: execute, record the
    /// usage, normalise the output and publish the completion snapshot.
    /// </summary>
    private async System.Threading.Tasks.Task<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput, string? error)> ExecuteWaveTaskAsync(
        DomainAgent agent,
        DomainTask task,
        SimpleExecutionContext context,
        TokenUsageTally tokenTally,
        System.Collections.Concurrent.ConcurrentBag<TaskExecutionSnapshot> taskSnapshots,
        CancellationToken cancellationToken)
    {
        LogStartingParallelExecutionOfTask(task.Id, agent.Id);
        await _hooks.TaskStartedAsync(
            CrewHookDispatcher.Started(task.Id.Value.ToString(), agent.Role.Value), cancellationToken)
            .ConfigureAwait(false);

        var result = await _executionService.ExecuteTaskAsync(
            agent, task, context, cancellationToken).ConfigureAwait(false);

        tokenTally.Record(result);

        // Guard against empty output (e.g., LLM call failed)
        var rawOutput = CrewRunOutcome.RawOutputOf(result);

        var domainOutput = DomainTaskOutput.Create(
            rawOutput: rawOutput,
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: result.Success,
            executionTime: result.ExecutionTime,
            structuredOutput: result.StructuredOutput);

        var appOutput = new ApplicationTaskOutput(
            TaskId: task.Id.Value.ToString(),
            AgentId: agent.Id.ToString(),
            Content: rawOutput,
            CompletedAt: DateTime.UtcNow,
            Success: result.Success,
            ExecutionTime: result.ExecutionTime,
            ToolsUsed: result.ToolsUsed);

        LogCompletedParallelExecutionOfTask(task.Id, result.Success);

        var snapshot = new TaskExecutionSnapshot
        {
            TaskId = task.Id.Value.ToString(),
            AgentRole = agent.Role.Value,
            Success = result.Success,
            Duration = result.ExecutionTime,
            CompletedAt = DateTimeOffset.UtcNow,
            ToolCallCount = result.ToolsUsed?.Count ?? 0,
            TokensUsed = result.TokensUsed,
            CacheHitTokens = result.CacheHitTokens,
            CacheMissTokens = result.CacheMissTokens,
        };
        taskSnapshots.Add(snapshot);
        await _hooks.TaskCompletedAsync(snapshot, cancellationToken).ConfigureAwait(false);

        return (domainOutput, appOutput, result.Error ?? result.LastError);
    }

    /// <summary>
    /// The tasks grouped into dependency waves: everything in a wave can run at once, and a
    /// wave starts only once every wave before it is done.
    /// <para>
    /// A dependency naming a task this crew does not carry is treated as already satisfied —
    /// the crew cannot wait for something it will never run, and refusing the whole crew over
    /// a stale id would be worse than running it. A genuine cycle is refused, naming the tasks
    /// caught in it: there is no order that satisfies it, and running them concurrently is the
    /// silence this change exists to remove.
    /// </para>
    /// </summary>
    private static List<List<DomainTask>> DependencyWaves(List<DomainTask> tasks)
    {
        var present = tasks.Select(t => t.Id).ToHashSet();
        var satisfied = new HashSet<TaskId>();
        var remaining = new List<DomainTask>(tasks);
        var waves = new List<List<DomainTask>>();

        while (remaining.Count > 0)
        {
            var wave = remaining
                .Where(t => t.Dependencies.All(d => !present.Contains(d) || satisfied.Contains(d)))
                .ToList();

            if (wave.Count == 0)
            {
                throw new InvalidOperationException(
                    "Circular task dependencies in this crew: "
                    + string.Join(", ", remaining.Select(t => t.Description.Value))
                    + ". No execution order satisfies them.");
            }

            waves.Add(wave);
            foreach (var task in wave)
            {
                satisfied.Add(task.Id);
                remaining.Remove(task);
            }
        }

        return waves;
    }

    /// <summary>
    /// Awaits every launched task, swallowing their outcomes — the barrier is about to
    /// report the crew-level failure, and a faulted sibling must neither mask it nor
    /// outlive it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Quiescing already-launched tasks before the terminal dispatch; their individual outcomes are already in the snapshots.")]
    private static async System.Threading.Tasks.Task SettleAsync(
        List<System.Threading.Tasks.Task<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput, string? error)>> tasks)
    {
        try
        {
            await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Individual failures were converted or are being reported by the caller.
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting parallel execution for crew {CrewId}")]
    private partial void LogStartingParallelExecutionForCrew(CrewId crewId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} not found, skipping")]
    private partial void LogTaskNotFoundSkipping(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Starting parallel execution of task {TaskId} with agent {AgentId}")]
    private partial void LogStartingParallelExecutionOfTask(TaskId taskId, AgentId agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Completed parallel execution of task {TaskId}, success: {Success}")]
    private partial void LogCompletedParallelExecutionOfTask(TaskId taskId, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Parallel execution of crew {CrewId} failed: {FailedTasks} task(s) did not succeed. {Reason}")]
    private partial void LogParallelExecutionFailedForCrew(CrewId crewId, int failedTasks, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} ({AgentRole}) skipped: it depends on task {DependencyId}, which did not succeed")]
    private partial void LogTaskSkippedAfterDependency(TaskId taskId, string agentRole, TaskId dependencyId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Parallel execution completed for crew {CrewId} in {Duration}")]
    private partial void LogParallelExecutionCompletedForCrew(CrewId crewId, TimeSpan duration);

}
