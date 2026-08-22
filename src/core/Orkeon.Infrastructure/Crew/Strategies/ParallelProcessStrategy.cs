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
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Parallel process strategy implementation.
/// Executes independent tasks concurrently.
/// </summary>
public sealed partial class ParallelProcessStrategy : IProcessStrategy
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentExecutionService _executionService;
    private readonly IMemoryScope _memoryScope;
    private readonly CrewHookDispatcher _hooks;
    private readonly ILogger<ParallelProcessStrategy> _logger;

    /// <summary>Initializes a new instance of <see cref="ParallelProcessStrategy"/>.</summary>
    /// <param name="taskRepository">The task repository.</param>
    /// <param name="agentRepository">The agent repository.</param>
    /// <param name="executionService">The agent execution service.</param>
    /// <param name="memoryScope">The memory scope.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="hook">Optional crew execution hook. May be null (BUS-03).</param>
    public ParallelProcessStrategy(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope,
        ILogger<ParallelProcessStrategy> logger,
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
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _hooks = new CrewHookDispatcher(hook, logger);
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

        // Load all agents
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId, cancellationToken).ConfigureAwait(false);
            if (agent != null) agents.Add(agent);
        }

        if (agents.Count == 0)
            throw new InvalidOperationException("No agents available for parallel execution");

        // Load all tasks and create execution pairs, using plan ordering
        var taskSnapshots = new System.Collections.Concurrent.ConcurrentBag<TaskExecutionSnapshot>();
        var executionTasks = new List<System.Threading.Tasks.Task<(DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput)>>();
        var taskIndex = 0;

        // Use plan tasks if available, otherwise fall back to crew tasks
        var plannedTasks = plan.GetTasksInOrder().ToList();
        var taskIds = plannedTasks.Count > 0
            ? plannedTasks.Select(pt => pt.TaskId)
            : crew.Tasks;

        foreach (var taskId in taskIds)
        {
            var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (task == null)
            {
                LogTaskNotFoundSkipping(taskId);
                continue;
            }

            // Assign agent round-robin
            var agent = agents[taskIndex % agents.Count];
            taskIndex++;

            cancellationToken.ThrowIfCancellationRequested();

            // Create context with input variables for parallel tasks (no shared previous outputs)
            var context = new SimpleExecutionContext(
                crew.Id,
                variables,
                _memoryScope,
                [],
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
                };
                taskSnapshots.Add(snapshot);
                await _hooks.TaskCompletedAsync(snapshot, cancellationToken).ConfigureAwait(false);

                return (domainOutput, appOutput);
            }));
        }

        // Wait for all tasks. One faulted task means WhenAll throws — the terminal event
        // must still go out, or a watcher sees a run frozen at its last completed sibling.
        (DomainTaskOutput domainOutput, ApplicationTaskOutput appOutput)[] results;
        try
        {
            results = await System.Threading.Tasks.Task.WhenAll(executionTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
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
