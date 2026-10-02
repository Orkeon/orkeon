using System.Collections.Concurrent;
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
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Sequential process strategy implementation.
/// Executes tasks one after another, in the declared order sorted on the tasks' dependencies
/// (<see cref="CrewTaskSequencer"/>) — with or without <c>planning: true</c>, whose plan reaches
/// each task in its prompt and never changes the order (GAP-31).
/// <para>
/// A task with <c>asyncExecution</c> is the exception, with CrewAI's semantics (GAP-22): it is
/// launched on its own flow and the next task starts at once; a task that depends on it waits for
/// it, then reads its output; the crew waits for every task it launched before it reports, and its
/// output stays the last declared task's. Its execution goes through the same service as a task of
/// a parallel wave, and it is recorded from this strategy's own flow — started when launched, ended
/// when waited for (<see cref="CrewRunOutcome"/> is not thread-safe).
/// </para>
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
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return ExecuteSequentialCoreAsync(crew, inputVariables, cancellationToken);
    }

    private async System.Threading.Tasks.Task<DomainCrewOutput> ExecuteSequentialCoreAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string>? inputVariables,
        CancellationToken cancellationToken)
    {
        LogStartingSequentialExecutionForCrew(crew.Id);

        var startedAt = DateTimeOffset.UtcNow;
        var startTime = startedAt.UtcDateTime;
        var variables = inputVariables != null
            ? new Dictionary<string, string>(inputVariables)
            : [];
        var run = new SequentialRun(
            new SimpleExecutionContext(crew.Id, variables, _memoryScope, [], cancellationToken),
            new CrewRunOutcome(_lifecycle, cancellationToken));

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

            _delegationProvider.UpdateExecutionContext(run.ContextNow());

            var taskIds = await CrewTaskSequencer.ResolveAsync(
                crew, _taskRepository, _logger, cancellationToken).ConfigureAwait(false);
            var agentIndex = 0;

            for (var position = 0; position < taskIds.Count; position++)
            {
                var taskId = taskIds[position];
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

                // A launched task this one depends on is waited for first (GAP-22): its outcome
                // decides whether this one runs, and its output joins the context it reads.
                await JoinDependenciesAsync(run, task).ConfigureAwait(false);

                if (run.Outcome.BlockingDependency(task) is { } blockedBy)
                    await SkipBlockedTaskAsync(run, position, task, agent, blockedBy).ConfigureAwait(false);
                else if (task.AsyncExecution)
                    await LaunchAsync(run, position, task, agent, cancellationToken).ConfigureAwait(false);
                else
                    await RunTaskAsync(run, position, task, agent, cancellationToken).ConfigureAwait(false);
            }

            // Nothing outlives the run: the crew waits for every task it launched (GAP-22),
            // recording them in declared order, so its error reads the same whichever ended first.
            foreach (var launched in run.Launched.ToList())
                await JoinAsync(run, launched).ConfigureAwait(false);

            var totalTime = DateTime.UtcNow - startTime;
            var domainResults = run.DomainResults;

            // The crew's output is the last declared task's, not the last one to finish.
            var finalOutput = domainResults.LastOrDefault()?.Output ?? string.Empty;

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
                domainResults, totalTime, metadata, finalOutput).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // The launched tasks observe the same token: they settle before anything is reported,
            // so none speaks after the terminal event. Then every task the cancellation caught is
            // cancelled, its agent failing it (GAP-21).
            await SettleAsync(run).ConfigureAwait(false);
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
            await SettleAsync(run).ConfigureAwait(false);
            await run.Outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.Value.ToString(), startedAt, run.TaskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// What one sequential pass accumulates: the outputs the run has waited for, by their place in
    /// the sequence; the tasks launched with <c>asyncExecution</c> and not yet waited for; the
    /// snapshots the hooks received; the token tally; and the outcome — failures, tasks that did not
    /// succeed, lifecycle.
    /// </summary>
    private sealed class SequentialRun
    {
        private readonly SortedList<int, (DomainTaskOutput Domain, ApplicationTaskOutput Application)> _outputs = new();
        private readonly List<LaunchedTask> _launched = [];

        public SequentialRun(SimpleExecutionContext root, CrewRunOutcome outcome)
        {
            Root = root;
            Outcome = outcome;
        }

        /// <summary>The run's root context: every task's context is derived from it (GAP-21, GAP-30).</summary>
        public SimpleExecutionContext Root { get; }

        /// <summary>
        /// The snapshots the hooks heard, in the order the tasks ended. A launched task reports its own
        /// from its own flow, hence a concurrent queue.
        /// </summary>
        public ConcurrentQueue<TaskExecutionSnapshot> TaskSnapshots { get; } = new();

        /// <summary>The run's token usage; thread-safe — a launched task records its own.</summary>
        public TokenUsageTally TokenTally { get; } = new();

        /// <summary>
        /// The failures so far, and the tasks that did not succeed: a task depending on one of
        /// them is skipped in its turn, so a broken step never runs the rest of the chain on a
        /// context that says "Task failed: …" where the deliverable it needed should have been
        /// (LLM-11). The rule is shared by the six modes (GAP-03), and so is the lifecycle of the
        /// tasks and agents it records (GAP-21). Used from the strategy's own flow only.
        /// </summary>
        public CrewRunOutcome Outcome { get; }

        /// <summary>The tasks launched with <c>asyncExecution</c> and not yet waited for, in declared order.</summary>
        public IReadOnlyList<LaunchedTask> Launched => _launched;

        /// <summary>The outputs of the run, in declared order.</summary>
        public List<DomainTaskOutput> DomainResults => [.. _outputs.Values.Select(o => o.Domain)];

        /// <summary>
        /// The context of a task starting now: the outputs the run has waited for, in declared order —
        /// a launched task's only once a task waited for it, so what a task reads never depends on
        /// timing. A copy: a task running alongside the run never sees the list move.
        /// </summary>
        public SimpleExecutionContext ContextNow() =>
            Root with { PreviousOutputs = [.. _outputs.Values.Select(o => o.Application)] };

        /// <summary>Records the outputs of the task at <paramref name="position"/> in the sequence.</summary>
        public void Fold(int position, DomainTaskOutput domain, ApplicationTaskOutput application) =>
            _outputs[position] = (domain, application);

        public void Launch(LaunchedTask launched) => _launched.Add(launched);

        public void Joined(LaunchedTask launched) => _launched.Remove(launched);
    }

    /// <summary>A task launched with <c>asyncExecution</c>: its place in the sequence, its agent, its execution.</summary>
    private sealed record LaunchedTask(
        int Position, Orkeon.Domain.Task.CrewTask Task, DomainAgent Agent, System.Threading.Tasks.Task<TaskRun> Execution);

    /// <summary>What one execution of a task produced, in every shape the run records.</summary>
    private sealed record TaskRun(
        Orkeon.Application.Interfaces.Services.TaskResult Result,
        DomainTaskOutput Domain,
        ApplicationTaskOutput Application,
        TaskExecutionSnapshot Snapshot);

    /// <summary>
    /// A task blocked by a dependency that did not succeed (LLM-11): recorded as skipped, never
    /// run — a failed output for the next tasks' context and the crew's result, and a snapshot
    /// marked <see cref="TaskExecutionSnapshot.Skipped"/> for the summary and the run events.
    /// No agent is asked anything, so nothing is started and no token is spent; the task is
    /// cancelled with the reason (GAP-21).
    /// </summary>
    private async System.Threading.Tasks.Task SkipBlockedTaskAsync(
        SequentialRun run, int position, Orkeon.Domain.Task.CrewTask task, DomainAgent agent, TaskId blockedBy)
    {
        var reason = await run.Outcome.RecordSkipAsync(task, agent.Role.Value, blockedBy).ConfigureAwait(false);
        LogTaskSkippedAfterDependency(task.Id, agent.Role, blockedBy);

        var (domainOutput, applicationOutput) = CrewRunOutcome.SkippedOutputs(task.Id, agent.Id.ToString(), blockedBy);
        run.Fold(position, domainOutput, applicationOutput);

        await ReportEndAsync(run, CrewRunOutcome.SkippedSnapshot(task.Id, agent.Role.Value, reason)).ConfigureAwait(false);
    }

    /// <summary>One task run by its agent while the run waits, its outcome folded into the pass.</summary>
    private async System.Threading.Tasks.Task RunTaskAsync(
        SequentialRun run, int position, Orkeon.Domain.Task.CrewTask task, DomainAgent agent, CancellationToken cancellationToken)
    {
        // The task is about to run: say so before asking the agent anything, so a
        // watcher shows it in progress instead of discovering it only once finished.
        await _hooks.TaskStartedAsync(
            CrewHookDispatcher.Started(task.Id.Value.ToString(), agent.Role.Value), CancellationToken.None)
            .ConfigureAwait(false);
        await run.Outcome.RecordStartAsync(task, agent).ConfigureAwait(false);

        var context = run.ContextNow();
        _delegationProvider.UpdateExecutionContext(context);
        var ran = await ExecuteAsync(task, agent, context, run.TokenTally, cancellationToken).ConfigureAwait(false);
        LogTaskCompletedSuccess(task.Id, ran.Result.Success);

        await RecordEndAsync(run, position, task, agent, ran).ConfigureAwait(false);
        await ReportEndAsync(run, ran.Snapshot).ConfigureAwait(false);
    }

    /// <summary>
    /// A task with <c>asyncExecution</c>: started now — its hook, its lifecycle —, then launched on its
    /// own flow, like a task of a parallel wave, and the run goes on. Its context holds what the run
    /// has waited for so far.
    /// </summary>
    private async System.Threading.Tasks.Task LaunchAsync(
        SequentialRun run, int position, Orkeon.Domain.Task.CrewTask task, DomainAgent agent, CancellationToken cancellationToken)
    {
        await _hooks.TaskStartedAsync(
            CrewHookDispatcher.Started(task.Id.Value.ToString(), agent.Role.Value), CancellationToken.None)
            .ConfigureAwait(false);
        await run.Outcome.RecordStartAsync(task, agent).ConfigureAwait(false);

        var context = run.ContextNow();
        LogLaunchingAsyncTask(task.Id);

        // Started whatever the token says: the execution observes it, and the outcome must hear
        // how the task ended — a launch that never ran would leave it running forever.
        var execution = System.Threading.Tasks.Task.Run(
            () => RunAlongsideAsync(task, agent, context, run, cancellationToken), CancellationToken.None);
        run.Launch(new LaunchedTask(position, task, agent, execution));
    }

    /// <summary>
    /// A launched task, on its own flow: executed, and reported to the hooks as it ends — they hear
    /// the tasks in the order they finish. Its delegations derive from its own context, not from the
    /// one the run has moved on to. Its outcome is recorded once a task waits for it.
    /// </summary>
    private async System.Threading.Tasks.Task<TaskRun> RunAlongsideAsync(
        Orkeon.Domain.Task.CrewTask task, DomainAgent agent, SimpleExecutionContext context, SequentialRun run,
        CancellationToken cancellationToken)
    {
        _delegationProvider.UseExecutionContextOnThisFlow(context);
        var ran = await ExecuteAsync(task, agent, context, run.TokenTally, cancellationToken).ConfigureAwait(false);
        LogTaskCompletedSuccess(task.Id, ran.Result.Success);
        await ReportEndAsync(run, ran.Snapshot).ConfigureAwait(false);
        return ran;
    }

    /// <summary>Waits for every launched task <paramref name="task"/> depends on, in declared order.</summary>
    private async System.Threading.Tasks.Task JoinDependenciesAsync(SequentialRun run, Orkeon.Domain.Task.CrewTask task)
    {
        foreach (var launched in run.Launched.Where(l => task.Dependencies.Contains(l.Task.Id)).ToList())
            await JoinAsync(run, launched).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a launched task and records how it ended, from the strategy's own flow: its outputs
    /// join the context of the tasks after, its success or failure the outcome. An execution that
    /// threw — a cancellation — interrupts the run.
    /// </summary>
    private async System.Threading.Tasks.Task JoinAsync(SequentialRun run, LaunchedTask launched)
    {
        LogWaitingForAsyncTask(launched.Task.Id);
        var ran = await launched.Execution.ConfigureAwait(false);
        run.Joined(launched);
        await RecordEndAsync(run, launched.Position, launched.Task, launched.Agent, ran).ConfigureAwait(false);
    }

    /// <summary>Folds a task's outputs into the pass and records its success or its failure.</summary>
    private static async System.Threading.Tasks.Task RecordEndAsync(
        SequentialRun run, int position, Orkeon.Domain.Task.CrewTask task, DomainAgent agent, TaskRun ran)
    {
        run.Fold(position, ran.Domain, ran.Application);
        if (ran.Result.Success)
            await run.Outcome.RecordSuccessAsync(task, ran.Domain).ConfigureAwait(false);
        else
            await run.Outcome.RecordFailureAsync(task, agent.Role.Value, ran.Result.Error ?? ran.Result.LastError).ConfigureAwait(false);
    }

    /// <summary>Hands a task's end to the hooks, when anything listens.</summary>
    private async System.Threading.Tasks.Task ReportEndAsync(SequentialRun run, TaskExecutionSnapshot snapshot)
    {
        if (!_hooks.HasHook)
            return;

        run.TaskSnapshots.Enqueue(snapshot);
        await _hooks.TaskCompletedAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for every launched task, whatever it ends on — the run is about to report its own
    /// interruption, and a task still running must neither outlive it nor mask it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Quiescing the launched tasks before the terminal dispatch; how each ended is the interruption's to record.")]
    private static async System.Threading.Tasks.Task SettleAsync(SequentialRun run)
    {
        try
        {
            await System.Threading.Tasks.Task.WhenAll(run.Launched.Select(l => l.Execution)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The interruption records every task still running.
        }
    }

    private async System.Threading.Tasks.Task<TaskRun> ExecuteAsync(
        Orkeon.Domain.Task.CrewTask task,
        DomainAgent agent,
        SimpleExecutionContext context,
        TokenUsageTally tokenTally,
        CancellationToken cancellationToken)
    {
        var executionResult = await _executionService.ExecuteTaskAsync(
            agent, task, context, cancellationToken).ConfigureAwait(false);
        tokenTally.Record(executionResult);

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

        var domainOutput = DomainTaskOutput.Create(
            rawOutput: rawOutput,
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: executionResult.Success,
            executionTime: executionResult.ExecutionTime,
            structuredOutput: executionResult.StructuredOutput,
            agentId: agent.Id.ToString());

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

        return new TaskRun(executionResult, domainOutput, appOutput, snapshot);
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
    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId} launched with asyncExecution: the run goes on without waiting for it")]
    private partial void LogLaunchingAsyncTask(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Waiting for async task {TaskId}")]
    private partial void LogWaitingForAsyncTask(TaskId taskId);

}
