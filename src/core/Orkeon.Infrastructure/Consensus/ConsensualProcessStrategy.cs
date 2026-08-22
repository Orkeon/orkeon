using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Crew;
using Orkeon.Infrastructure.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew.Strategies;
using IProcessStrategy = Orkeon.Domain.Crew.IProcessStrategy;
using IAgentRepository = Orkeon.Domain.Agent.IAgentRepository;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Consensual process strategy where agents independently execute each task,
/// then reach consensus through voting. If consensus is not reached, optional
/// discussion rounds allow agents to reconsider with context from others' results.
/// </summary>
/// <remarks>
/// Also implements <see cref="IProcessStrategy"/> so that
/// <c>ProcessStrategyFactory</c> routes <c>ProcessType.Consensual</c> like every other
/// process type (R3.3): <see cref="ExecuteSequentialAsync"/> maps to the consensual
/// voting pipeline; the other modes have dedicated strategies.
/// </remarks>
public sealed partial class ConsensualProcessStrategy : IConsensualProcessStrategy, IProcessStrategy
{
    private readonly IVotingStrategy _votingStrategy;
    private readonly IAgentExecutionService _executionService;
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IMemoryScope _memoryScope;
    private readonly ILogger<ConsensualProcessStrategy> _logger;
    private readonly ConsensualProcessOptions _options;
    private readonly CrewHookDispatcher _hooks;

    /// <summary>Initializes a new instance of <see cref="ConsensualProcessStrategy"/>.</summary>
    /// <param name="votingStrategy">The voting strategy.</param>
    /// <param name="executionService">The agent execution service.</param>
    /// <param name="taskRepository">The task repository.</param>
    /// <param name="agentRepository">The agent repository.</param>
    /// <param name="memoryScope">The memory scope.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The consensual process options.</param>
    /// <param name="hook">Optional crew execution hook. May be null (BUS-03).</param>
    public ConsensualProcessStrategy(
        IVotingStrategy votingStrategy,
        IAgentExecutionService executionService,
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IMemoryScope memoryScope,
        ILogger<ConsensualProcessStrategy> logger,
        IOptions<ConsensualProcessOptions> options,
        ICrewExecutionHook? hook = null)
    {
        ArgumentNullException.ThrowIfNull(votingStrategy);
        _votingStrategy = votingStrategy;
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(memoryScope);
        _memoryScope = memoryScope;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _hooks = new CrewHookDispatcher(hook, logger);
    }

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteConsensualAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(plan);
        return ExecuteConsensualCoreAsync(crew, plan, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs the consensual voting pipeline over the planned tasks. Input variables are
    /// accepted for signature compatibility but are not interpolated by the consensual
    /// pipeline (iso with the historical routing).
    /// </remarks>
    public Task<DomainCrewOutput> ExecuteSequentialAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => ExecuteConsensualAsync(crew, plan, cancellationToken);

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(
        DomainCrew crew,
        AgentId managerAgentId,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use HierarchicalProcessStrategy for hierarchical orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use ParallelProcessStrategy for parallel orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteAutonomousAsync(
        DomainCrew crew,
        AgentExecutionBudget budget,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    private async Task<DomainCrewOutput> ExecuteConsensualCoreAsync(
        DomainCrew crew,
        DomainExecutionPlan plan,
        CancellationToken ct)
    {
        LogStartingConsensualExecutionForCrew(crew.Id);

        var startTime = DateTime.UtcNow;
        var domainResults = new List<DomainTaskOutput>();
        var taskSnapshots = new List<TaskExecutionSnapshot>();
        var applicationOutputs = new List<ApplicationTaskOutput>();

        // Token telemetry propagation (R10.8) — same metadata channel as Sequential.
        // Consensus burns tokens with EVERY agent in EVERY voting round (plus fallback
        // re-execution), so the real cost is the sum of all executions, not just the
        // winning result.
        var tokenTally = new TokenUsageTally();

        // The terminal event goes out on EVERY exit — setup included: "consensus not
        // reached" was the only failure this mode reported, and a throwing round, a Ctrl+C
        // or an agent-less crew escaped silently.
        try
        {
        // Load all agents
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId, ct).ConfigureAwait(false);
            if (agent != null) agents.Add(agent);
        }

        if (agents.Count == 0)
            throw new InvalidOperationException("No agents available for consensual execution");

        // Execute each task with consensus, using plan ordering if available
        var plannedTasks = plan.GetTasksInOrder().ToList();
        var taskIds = plannedTasks.Count > 0
            ? plannedTasks.Select(pt => pt.TaskId)
            : crew.Tasks;

        foreach (var taskId in taskIds)
        {
            ct.ThrowIfCancellationRequested();

            var task = await _taskRepository.GetByIdAsync(taskId, ct).ConfigureAwait(false);
            if (task == null)
            {
                LogTaskNotFoundSkipping(taskId);
                continue;
            }

            LogStartingConsensualExecutionForTask(taskId);

            var taskResult = await ExecuteTaskWithConsensusAsync(
                crew, task, agents, applicationOutputs, tokenTally, ct).ConfigureAwait(false);

            if (taskResult == null)
            {
                // Fallback = Fail was triggered. Tokens were still consumed by the
                // voting rounds — propagate the measured cost with the failure.
                var totalTime = DateTime.UtcNow - startTime;
                await _hooks.CrewFailedAsync(
                    CrewHookDispatcher.Snapshot(
                        crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Failed,
                        $"Consensus could not be reached for task {taskId}"),
                    cause: null, ct).ConfigureAwait(false);

                return DomainCrewOutput.CreateFailure(
                    error: $"Consensus could not be reached for task {taskId}",
                    taskOutputs: domainResults,
                    executionTime: totalTime,
                    metadata: tokenTally
                        .WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder())
                        .Build());
            }

            // Build application output for context propagation
            var appOutput = new ApplicationTaskOutput(
                TaskId: task.Id.Value.ToString(),
                AgentId: "consensus",
                Content: taskResult.Output,
                CompletedAt: DateTime.UtcNow,
                Success: taskResult.Success,
                ExecutionTime: taskResult.ExecutionTime,
                ToolsUsed: taskResult.ToolsUsed);
            applicationOutputs.Add(appOutput);

            // Build domain output
            domainResults.Add(DomainTaskOutput.Create(
                rawOutput: taskResult.Output,
                format: "text",
                formattedOutput: null,
                taskId: task.Id,
                success: taskResult.Success,
                executionTime: taskResult.ExecutionTime,
                structuredOutput: taskResult.StructuredOutput));

            LogTaskCompletedViaConsensusSuccess(taskId, taskResult.Success);

            var snapshot = new TaskExecutionSnapshot
            {
                TaskId = task.Id.Value.ToString(),
                // Consensus has no single author: the vote is the agent.
                AgentRole = "consensus",
                Success = taskResult.Success,
                Duration = taskResult.ExecutionTime,
                CompletedAt = DateTimeOffset.UtcNow,
                ToolCallCount = taskResult.ToolsUsed?.Count ?? 0,
            };
            taskSnapshots.Add(snapshot);
            await _hooks.TaskCompletedAsync(snapshot, ct).ConfigureAwait(false);
        }
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

        var totalExecutionTime = DateTime.UtcNow - startTime;
        var finalOutput = domainResults.LastOrDefault()?.Output ?? string.Empty;

        LogConsensualExecutionCompletedForCrew(crew.Id, totalExecutionTime);

        await _hooks.CrewCompletedAsync(
            CrewHookDispatcher.Snapshot(
                crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Completed),
            ct).ConfigureAwait(false);

        return DomainCrewOutput.CreateSuccess(
            output: finalOutput,
            structuredOutput: null,
            taskOutputs: domainResults,
            executionTime: totalExecutionTime,
            metadata: tokenTally
                .WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder())
                .Build());
    }

    private async Task<TaskResult?> ExecuteTaskWithConsensusAsync(
        DomainCrew crew,
        CrewTask task,
        List<DomainAgent> agents,
        List<ApplicationTaskOutput> previousOutputs,
        TokenUsageTally tokenTally,
        CancellationToken ct)
    {
        var maxRounds = _options.MaxVotingRounds;
        Dictionary<string, TaskResult>? previousResults = null;

        for (int round = 1; round <= maxRounds; round++)
        {
            LogTaskStartingVotingRound(task.Id, round, maxRounds);

            // Execute task with all agents in parallel
            var agentResults = await ExecuteWithAllAgentsAsync(
                crew, task, agents, previousOutputs, previousResults, ct).ConfigureAwait(false);

            // Record the cost of the whole round: every agent execution consumed tokens,
            // whichever result ends up winning the vote.
            foreach (var agentResult in agentResults.Values)
                tokenTally.Record(agentResult);

            // Create votes from results
            var votes = CreateVotesFromResults(agents, agentResults);

            // Tally votes
            var voteResult = await _votingStrategy.TallyVotesAsync(
                votes, _options.VotingOptions, ct).ConfigureAwait(false);

            LogTaskRoundResultConsensusreachedWinningchoice(task.Id, round, voteResult.ConsensusReached, voteResult.WinningChoice ?? "(none)", voteResult.AgreementScore);

            if (voteResult.ConsensusReached && voteResult.WinningChoice != null
                && agentResults.TryGetValue(voteResult.WinningChoice, out var winningResult))
            {
                // Return the winning result
                return winningResult;
            }

            // No consensus, prepare for next round
            if (round < maxRounds && _options.EnableDiscussion)
            {
                LogTaskNoConsensusInRound(task.Id, round);
                previousResults = agentResults;
            }
        }

        // Max rounds exhausted, apply fallback
        return await ApplyFallbackAsync(task, agents, previousOutputs, tokenTally, ct).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-agent fault barrier: a single agent's task failure is converted into a failed TaskResult so the remaining agents' votes are still tallied.")]
    private async Task<Dictionary<string, TaskResult>> ExecuteWithAllAgentsAsync(
        DomainCrew crew,
        CrewTask task,
        List<DomainAgent> agents,
        List<ApplicationTaskOutput> previousOutputs,
        Dictionary<string, TaskResult>? discussionContext,
        CancellationToken ct)
    {
        var results = new Dictionary<string, TaskResult>();
        var executionTasks = new List<(string agentKey, System.Threading.Tasks.Task<TaskResult> task)>();

        foreach (var agent in agents)
        {
            var agentKey = agent.Id.ToString();

            // Build context with optional discussion context from previous round
            var variables = new Dictionary<string, string>();
            if (discussionContext != null)
            {
                // Add other agents' previous results as discussion context
                var otherResults = discussionContext
                    .Where(kvp => kvp.Key != agentKey)
                    .Select(kvp => $"Agent {kvp.Key}: {kvp.Value.Output}")
                    .ToList();

                if (otherResults.Count > 0)
                {
                    variables["discussion_context"] = string.Join("\n---\n", otherResults);
                }
            }

            var context = new SimpleExecutionContext(
                crew.Id,
                variables,
                _memoryScope,
                previousOutputs,
                ct);

            var capturedAgent = agent;
            executionTasks.Add((agentKey, System.Threading.Tasks.Task.Run(async () =>
            {
                return await _executionService.ExecuteTaskAsync(
                    capturedAgent, task, context, ct).ConfigureAwait(false);
            }, ct)));
        }

        // Wait for all agents to complete
        await System.Threading.Tasks.Task.WhenAll(executionTasks.Select(t => t.task)).ConfigureAwait(false);

        foreach (var (agentKey, executionTask) in executionTasks)
        {
            try
            {
                results[agentKey] = await executionTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogAgentFailedToExecuteTask(ex, agentKey);
                results[agentKey] = new TaskResult(
                    false, string.Empty, null,
                    [],
                    TimeSpan.Zero,
                    ex.Message);
            }
        }

        return results;
    }

    private static List<Vote> CreateVotesFromResults(
        List<DomainAgent> agents,
        Dictionary<string, TaskResult> agentResults)
    {
        var votes = new List<Vote>();

        foreach (var agent in agents)
        {
            var agentKey = agent.Id.ToString();
            if (!agentResults.TryGetValue(agentKey, out var result))
                continue;

            // Each agent's output is their "vote" (choice)
            // The choice is the agent key so we can retrieve the full result later
            votes.Add(new Vote
            {
                VoterId = agentKey,
                VoterRole = agent.Role.ToString(),
                Choice = agentKey,
                Confidence = result.Success ? 1.0f : 0.1f,
                Weight = 1.0f,
                Justification = result.Output,
                Timestamp = DateTime.UtcNow
            });
        }

        return votes;
    }

    private async Task<TaskResult?> ApplyFallbackAsync(
        CrewTask task,
        List<DomainAgent> agents,
        List<ApplicationTaskOutput> previousOutputs,
        TokenUsageTally tokenTally,
        CancellationToken ct)
    {
        LogTaskMaxVotingRoundsExhausted(task.Id, _options.FallbackStrategy);

        return _options.FallbackStrategy switch
        {
            ConsensusFallback.AcceptBestScore => await AcceptBestScoreAsync(task, agents, previousOutputs, tokenTally, ct).ConfigureAwait(false),
            ConsensusFallback.Fail => null,
            ConsensusFallback.ManagerDecision => await AcceptBestScoreAsync(task, agents, previousOutputs, tokenTally, ct).ConfigureAwait(false),
            _ => null
        };
    }

    private async Task<TaskResult> AcceptBestScoreAsync(
        CrewTask task,
        List<DomainAgent> agents,
        List<ApplicationTaskOutput> previousOutputs,
        TokenUsageTally tokenTally,
        CancellationToken ct)
    {
        // Re-execute with the first agent and accept the result
        var agent = agents.First();
        var context = new SimpleExecutionContext(
            CrewId.Create(),
            [],
            _memoryScope,
            previousOutputs,
            ct);

        var result = await _executionService.ExecuteTaskAsync(agent, task, context, ct).ConfigureAwait(false);
        tokenTally.Record(result);
        return result;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting consensual execution for crew {CrewId}")]
    private partial void LogStartingConsensualExecutionForCrew(CrewId crewId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} not found, skipping")]
    private partial void LogTaskNotFoundSkipping(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Starting consensual execution for task {TaskId}")]
    private partial void LogStartingConsensualExecutionForTask(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId} completed via consensus, success: {Success}")]
    private partial void LogTaskCompletedViaConsensusSuccess(TaskId taskId, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Consensual execution completed for crew {CrewId} in {Duration}")]
    private partial void LogConsensualExecutionCompletedForCrew(CrewId crewId, TimeSpan duration);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId}: Starting voting round {Round}/{MaxRounds}")]
    private partial void LogTaskStartingVotingRound(TaskId taskId, int round, int maxRounds);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId}: Round {Round} result - ConsensusReached: {Consensus}, WinningChoice: {Winner}, AgreementScore: {Score:F1}%")]
    private partial void LogTaskRoundResultConsensusreachedWinningchoice(TaskId taskId, int round, bool consensus, string winner, double score);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId}: No consensus in round {Round}, starting discussion round")]
    private partial void LogTaskNoConsensusInRound(TaskId taskId, int round);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Agent {AgentId} failed to execute task")]
    private partial void LogAgentFailedToExecuteTask(Exception ex, string agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId}: Max voting rounds exhausted, applying fallback: {Fallback}")]
    private partial void LogTaskMaxVotingRoundsExhausted(TaskId taskId, ConsensusFallback fallback);

}
