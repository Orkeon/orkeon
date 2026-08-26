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
using DomainExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
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

    /// <summary>Initializes a new instance of <see cref="ParallelProcessStrategy"/>.</summary>
    /// <param name="taskRepository">The task repository.</param>
    /// <param name="agentRepository">The agent repository.</param>
    /// <param name="executionService">The agent execution service.</param>
    /// <param name="memoryScope">The memory scope.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="agentSelector">Who runs a task that names no agent. Null means round-robin.</param>
    /// <param name="hook">Optional crew execution hook. May be null (BUS-03).</param>
    public ParallelProcessStrategy(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope,
        ILogger<ParallelProcessStrategy> logger,
        ICrewExecutionHook? hook = null,
        TaskAgentSelector? agentSelector = null)
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
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, DomainExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Sequential execution is not supported by ParallelProcessStrategy. " +
            "Use SequentialProcessStrategy instead.");
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Hierarchical execution is not supported by ParallelProcessStrategy. " +
            "Use HierarchicalProcessStrategy instead.");
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, DomainExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(plan);
        return ExecuteParallelCoreAsync(crew, plan, inputVariables, cancellationToken);
    }

    private async System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelCoreAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingParallelExecutionForCrew(crew.Id);

        var startTime = DateTime.UtcNow;
        // Token telemetry propagation (R10.8) — same metadata channel as Sequential.
        // The tally is thread-safe: tasks record their usage concurrently.
        var tokenTally = new TokenUsageTally();
        var variables = inputVariables != null
            ? new Dictionary<string, string>(inputVariables)
            : [];

        var taskSnapshots = new System.Collections.Concurrent.ConcurrentBag<TaskExecutionSnapshot>();
        var executionTasks = new List<System.Threading.Tasks.Task<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput)>>();

        // The barrier covers setup AND the fan-out loop, not only the WhenAll: a cancellation
        // firing mid-fan-out used to escape with tasks 1..n-1 already launched — no terminal
        // event, and orphans still emitting task.completed after the strategy had returned.
        var results = new List<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput)>();
        try
        {
        // Load all agents
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId, cancellationToken).ConfigureAwait(false);
            if (agent != null) agents.Add(agent);
        }

        if (agents.Count == 0)
            throw new InvalidOperationException("No agents available for parallel execution");

        var taskIndex = 0;

        // Use plan tasks if available, otherwise fall back to crew tasks
        var plannedTasks = plan.GetTasksInOrder().ToList();
        var taskIds = plannedTasks.Count > 0
            ? plannedTasks.Select(pt => pt.TaskId)
            : crew.Tasks;

        var tasks = new List<DomainTask>();
        foreach (var taskId in taskIds)
        {
            var loaded = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (loaded == null)
            {
                LogTaskNotFoundSkipping(taskId);
                continue;
            }

            tasks.Add(loaded);
        }

        // Waves, not one flat fan-out. A crew declaring `dependencies:` used to have them
        // ignored here: every task started at once, so a synthesis task ran against an empty
        // context while the tasks it consumes were still running, and reported success on the
        // nothing it had. Tasks with no unmet dependency go together; the next wave starts
        // when they are done, with their outputs in context. A crew declaring no dependency
        // is one wave — exactly the previous behaviour.
        var completedOutputs = new List<ApplicationTaskOutput>();

        foreach (var wave in DependencyWaves(tasks))
        {
            executionTasks.Clear();

            // Snapshot what the previous waves produced: every task in this wave reads the
            // same context, and the list must not be mutated while they run.
            var previousOutputs = completedOutputs.ToList();

            foreach (var task in wave)
            {
                // The agent the crew declared, round-robin only when it declared none — the same
                // choice Sequential and Graph make. This mode used to take loop order alone, so a
                // YAML `agent:` was silently ignored in parallel mode and nowhere else.
                var agent = await _agentSelector
                    .ForTaskAsync(task, agents, taskIndex++, cancellationToken)
                    .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                var context = new SimpleExecutionContext(
                    crew.Id,
                    variables,
                    _memoryScope,
                    previousOutputs,
                    cancellationToken);

                var capturedTask = task;
                var capturedAgent = agent;

                executionTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                {
                LogStartingParallelExecutionOfTask(capturedTask.Id, capturedAgent.Id);

                var result = await _executionService.ExecuteTaskAsync(
                    capturedAgent, capturedTask, context, cancellationToken).ConfigureAwait(false);

                tokenTally.Record(result);

                // Guard against empty output (e.g., LLM call failed)
                string rawOutput;
                if (!string.IsNullOrEmpty(result.Output))
                    rawOutput = result.Output;
                else if (result.Success)
                    rawOutput = "(no output)";
                else
                    rawOutput = $"Task failed: {result.Error ?? "unknown error"}";

                var domainOutput = DomainTaskOutput.Create(
                    rawOutput: rawOutput,
                    format: "text",
                    formattedOutput: null,
                    taskId: capturedTask.Id,
                    success: result.Success,
                    executionTime: result.ExecutionTime,
                    structuredOutput: result.StructuredOutput);

                var appOutput = new ApplicationTaskOutput(
                    TaskId: capturedTask.Id.Value.ToString(),
                    AgentId: capturedAgent.Id.ToString(),
                    Content: rawOutput,
                    CompletedAt: DateTime.UtcNow,
                    Success: result.Success,
                    ExecutionTime: result.ExecutionTime,
                    ToolsUsed: result.ToolsUsed);

                LogCompletedParallelExecutionOfTask(capturedTask.Id, result.Success);

                var snapshot = new TaskExecutionSnapshot
                {
                    TaskId = capturedTask.Id.Value.ToString(),
                    AgentRole = capturedAgent.Role.Value,
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

                return (domainOutput, appOutput);
                }));
            }

            // Wait for this wave. One faulted task means WhenAll throws — the terminal event
            // must still go out, or a watcher sees a run frozen at its last completed sibling.
            var waveResults = await System.Threading.Tasks.Task.WhenAll(executionTasks).ConfigureAwait(false);

            results.AddRange(waveResults);
            completedOutputs.AddRange(waveResults.Select(r => r.appOutput));
        }
        }
        catch (OperationCanceledException)
        {
            // Let the already-launched tasks settle before the terminal event: they observe
            // the same token, and a task.completed emitted AFTER the terminal event would
            // read as a run speaking from beyond its own grave.
            await SettleAsync(executionTasks).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Canceled, "Execution was cancelled."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await SettleAsync(executionTasks).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var domainResults = results.Select(r => r.domainOutput).ToList();
        var totalTime = DateTime.UtcNow - startTime;

        // Aggregate: combine all outputs
        var allOutputs = string.Join("\n\n", domainResults.Select(r => r.Output));

        LogParallelExecutionCompletedForCrew(crew.Id, totalTime);

        await _hooks.CrewCompletedAsync(
            CrewHookDispatcher.Snapshot(
                crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Completed),
            cancellationToken).ConfigureAwait(false);

        return DomainCrewOutput.CreateSuccess(
            output: allOutputs,
            structuredOutput: null,
            taskOutputs: domainResults,
            executionTime: totalTime,
            metadata: tokenTally
                .WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder())
                .Build());
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
        List<System.Threading.Tasks.Task<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput)>> tasks)
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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Parallel execution completed for crew {CrewId} in {Duration}")]
    private partial void LogParallelExecutionCompletedForCrew(CrewId crewId, TimeSpan duration);

}
