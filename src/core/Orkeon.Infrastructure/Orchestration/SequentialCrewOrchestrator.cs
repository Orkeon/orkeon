using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Interfaces;
using Orkeon.Domain.Task;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Crew.Execution;
using Orkeon.Infrastructure.Crew.Strategies;
// Resolve ambiguous references
using TaskOutput = Orkeon.Application.Execution.TaskOutput;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;

namespace Orkeon.Infrastructure.Orchestration;

/// <summary>
/// Simplified crew orchestration service focused on orchestration only.
/// All business logic has been moved to the domain layer.
/// </summary>
public partial class SequentialCrewOrchestrator : ICrewOrchestrationService
{
    private readonly ICrewRepository _crewRepository;
    private readonly ILogger<SequentialCrewOrchestrator> _logger;
    private readonly ICrewExecutionStateManager _stateManager;
    private readonly IProcessStrategyFactory _processStrategyFactory;
    private readonly IStreamingAgentExecutionService? _streamingService;
    private readonly IAgentRepository? _agentRepository;
    private readonly ICheckpointManager? _checkpointManager;
    private readonly IExecutionPlanParser _executionPlanParser;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly Orkeon.Application.Memory.CrewMemoryProviderRegistry? _memoryProviderRegistry;
    private readonly Orkeon.Application.EventHub.IEventHubCallerContext? _hubCallerContext;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? _llmProfiles;
    private readonly IMemoryCoordinator? _memoryCoordinator;
    private readonly ITaskRepository? _taskRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="SequentialCrewOrchestrator"/>.
    /// </summary>
    /// <remarks>
    /// Parameters exceed threshold due to DI injection requirements for optional services. The
    /// <paramref name="memoryCoordinator"/> refuses, before the first task, a crew with
    /// <c>memory: true</c> whose memory cannot work (GAP-30); without one, nothing is checked. The
    /// <paramref name="taskRepository"/> and <paramref name="agentRepository"/> show the crew's planner
    /// its tasks and agents (GAP-31): a crew with <c>planning: true</c> needs both.
    /// </remarks>
#pragma warning disable S107 // Methods should not have too many parameters — DI constructor with optional services
    public SequentialCrewOrchestrator(
        ICrewRepository crewRepository,
        ILogger<SequentialCrewOrchestrator> logger,
        ICrewExecutionStateManager stateManager,
        IProcessStrategyFactory processStrategyFactory,
        IExecutionPlanParser executionPlanParser,
        IDomainEventDispatcher domainEventDispatcher,
        IStreamingAgentExecutionService? streamingService = null,
        IAgentRepository? agentRepository = null,
        ICheckpointManager? checkpointManager = null,
        Orkeon.Application.Memory.CrewMemoryProviderRegistry? memoryProviderRegistry = null,
        Orkeon.Application.EventHub.IEventHubCallerContext? hubCallerContext = null,
        Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? llmProfiles = null,
        IMemoryCoordinator? memoryCoordinator = null,
        ITaskRepository? taskRepository = null)
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(crewRepository);
        _crewRepository = crewRepository;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(stateManager);
        _stateManager = stateManager;
        ArgumentNullException.ThrowIfNull(processStrategyFactory);
        _processStrategyFactory = processStrategyFactory;
        ArgumentNullException.ThrowIfNull(executionPlanParser);
        _executionPlanParser = executionPlanParser;
        ArgumentNullException.ThrowIfNull(domainEventDispatcher);
        _domainEventDispatcher = domainEventDispatcher;
        _streamingService = streamingService;
        _agentRepository = agentRepository;
        _checkpointManager = checkpointManager;
        _memoryProviderRegistry = memoryProviderRegistry;
        _hubCallerContext = hubCallerContext;
        _llmProfiles = llmProfiles;
        _memoryCoordinator = memoryCoordinator;
        _taskRepository = taskRepository;
    }

    /// <summary>
    /// Kickoff Async.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Crew-execution fault barrier: any strategy failure is logged, the checkpoint is marked failed, and a failed CrewOutput is returned so the orchestrator surfaces the error as a result rather than throwing to the caller.")]
    public Task<CrewOutput> KickoffAsync(
        CrewId crewId,
        CrewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return KickoffCoreAsync();

        async Task<CrewOutput> KickoffCoreAsync()
        {
        var stopwatch = Stopwatch.StartNew();

        if (crewId == null)
        {
            LogCrewIdNull();
            stopwatch.Stop();
            return new CrewOutput(
                FinalOutput: "Crew execution failed: CrewId cannot be null",
                TaskOutputs: [],
                Duration: stopwatch.Elapsed,
                TokensUsed: null) // nothing executed — nothing was measured
            { Succeeded = false };
        }

        LogOrchestratingCrewExecution(crewId);

        string? sessionId = null;
        Orkeon.Domain.Crew.Crew? crew = null;

        try
        {
            // Load crew from repository
            crew = await _crewRepository.GetByIdAsync(crewId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Crew {crewId} not found");

            // Record whether the crew remembers (memory:, GAP-30), the memory provider it declared,
            // which the memory subsystem resolves to a concrete IMemoryProvider for this run
            // (P2-O-02), and its name, the scope of its long-term memory in that shared store
            // (GAP-20). Idempotent.
            _memoryProviderRegistry?.Record(crew.Id, crew.MemoryProvider, crew.Name, crew.MemoryEnabled);

            // Start checkpoint session if checkpoint manager is available
            if (_checkpointManager != null)
            {
                sessionId = await _checkpointManager.StartSessionAsync(crewId.ToString(), cancellationToken).ConfigureAwait(false);
                LogCheckpointSessionStarted(sessionId, crewId);
            }

            // Validate and start execution (state transition in domain)
            crew.ValidateCanKickoff();
            crew.StartExecution();

            // Get the appropriate process strategy (every process type, including
            // Consensual, routes through the factory — R3.3)
            var processStrategy = _processStrategyFactory.CreateStrategy(crew.ProcessType);

            // Stamp the crew's identity on everything the run touches: the EventHub reads
            // the ambient caller to source its messages, and the ACL is blind — every sender
            // looks like CrewId.System — unless someone pushes it here (HUB-03). AsyncLocal,
            // so it flows through strategies, agents and tools alike.
            var domainOutput = await RunWithCrewIdentityAsync(
                crew.Id,
                () => ExecuteAndCompleteAsync(crew, processStrategy, input, cancellationToken))
                .ConfigureAwait(false);

            // Checkpoint each task output
            if (_checkpointManager != null && sessionId != null)
            {
                await CheckpointTaskOutputsAsync(sessionId, domainOutput, cancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();

            // Simple orchestration: convert domain result to application result.
            // Real token telemetry is propagated from the strategy via domain metadata  —
            // when the strategy collected no token data, TokensUsed stays null so that
            // consumers can distinguish "not measured" from a genuine zero-cost run
            // (R10.8 / MAT-004 — no fabricated TokenUsage(0,0,0)).
            return new CrewOutput(
                FinalOutput: domainOutput.Output,
                TaskOutputs: domainOutput.TaskOutputs?.Select(ConvertTaskOutput).ToList() ?? [],
                Duration: stopwatch.Elapsed,
                TokensUsed: ExtractTokenUsage(domainOutput))
            {
                Succeeded = domainOutput.Success,
                Error = domainOutput.Success ? null : domainOutput.Error,
            };
        }
        catch (Exception ex)
        {
            LogCrewExecutionError(ex);

            if (_checkpointManager != null && sessionId != null)
            {
                await _checkpointManager.MarkFailedAsync(sessionId, "crew-execution", ex, cancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();

            return new CrewOutput(
                FinalOutput: $"Crew execution failed: {ex.Message}",
                TaskOutputs: [],
                Duration: stopwatch.Elapsed,
                TokensUsed: null) // failed before telemetry could be collected
            { Succeeded = false, Error = ex.Message };
        }
        finally
        {
            // Success, failure and cancellation all end here: the crew's queued events
            // (construction events, then Started and Completed/Failed) go to the
            // IDomainEventHandler<T> registrations once, and the aggregate is emptied.
            if (crew is not null)
                await DispatchCrewEventsAsync(crew).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    /// Hands the crew's queued domain events to <see cref="IDomainEventDispatcher"/> and empties
    /// the aggregate. Dispatched directly rather than through <c>IUnitOfWork</c>, which tracks a
    /// single aggregate per scope. A handler that throws is logged and skipped: an observer never
    /// changes the run's output nor replaces its exception, and the events after it still go out.
    /// The run's token is not passed — a cancelled run still reports its failure.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Observer fault barrier: a failing domain-event handler must not alter the crew run's result, like CrewHookDispatcher.")]
    private async System.Threading.Tasks.Task DispatchCrewEventsAsync(Orkeon.Domain.Crew.Crew crew)
    {
        DomainEvent[] pending;
        lock (crew)
        {
            pending = [.. crew.DomainEvents];
            crew.ClearDomainEvents();
        }

        foreach (var domainEvent in pending)
        {
            try
            {
                await _domainEventDispatcher.DispatchAsync(domainEvent, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogDomainEventHandlerFailed(ex, domainEvent.GetType().Name, crew.Id);
            }
        }
    }

    /// <summary>
    /// Runs planning (when enabled), executes the process strategy inside the run's plan scope, and
    /// transitions the crew to its completed state. On any failure the crew is transitioned to failed
    /// and the exception is rethrown so the outer fault barrier can surface it as a failed
    /// <see cref="CrewOutput"/>.
    /// </summary>
    private async System.Threading.Tasks.Task<DomainCrewOutput> ExecuteAndCompleteAsync(
        Orkeon.Domain.Crew.Crew crew,
        IProcessStrategy processStrategy,
        CrewInput input,
        CancellationToken cancellationToken)
    {
        // Every LLM call of the run is metered for this crew, whichever part of the run makes
        // it — the planner, the manager, an agent, a tool (STUDIO-42). The narrower scopes
        // opened below say what the work was; this one says whose run it is.
        using var usageScope = Orkeon.Application.Interfaces.Ports.LlmUsageScope.Begin(crewId: crew.Id.ToString());

        try
        {
            // A crew that remembers is refused before anything is asked of a model when its memory
            // cannot work: no embedder, a refused key, an unreachable store, a vector of the wrong
            // dimension (GAP-30). During the run, a memory that fails is only a warning.
            if (crew.MemoryEnabled && _memoryCoordinator is not null)
                await _memoryCoordinator.EnsureReadyAsync(crew.Id, cancellationToken).ConfigureAwait(false);

            // Extract string variables from input for template interpolation
            var stringVariables = PromptVariables(input);

            // One step-by-step plan per task when the crew plans (GAP-31), before the first task.
            var plan = await PlanIfAskedAsync(crew, stringVariables, cancellationToken).ConfigureAwait(false);

            // Execute according to process type. The plan reaches each task through the run's scope,
            // read where every mode's executions compose their prompt — no strategy carries it. Opened
            // on every run, empty without planning, so a run nested in a task sees its own plan only.
            DomainCrewOutput domainOutput;
            using (CrewPlanScope.Begin(plan))
            {
                domainOutput = await ExecuteDomainStrategyAsync(
                    crew, processStrategy, stringVariables, cancellationToken).ConfigureAwait(false);
            }

            // Transition to completed state
            var completedTasks = domainOutput.TaskOutputs?.Count(t => t.Success) ?? 0;
            var failedTasks = domainOutput.TaskOutputs?.Count(t => !t.Success) ?? 0;
            crew.CompleteExecution(completedTasks, failedTasks);

            return domainOutput;
        }
        catch (Exception innerEx)
        {
            crew.FailExecution(innerEx.Message, innerEx);
            throw;
        }
    }

    /// <summary>
    /// The crew's plan when it asks for one (<c>planning: true</c>, <c>.Planning(true)</c>,
    /// <c>crewBuilder().planning()</c>): one step-by-step plan per task, made by the crew's planning
    /// provider when C# set one (<c>WithPlanningLlm</c>), else by the host's default profile — where
    /// the planner stays (GAP-29, GAP-19) — from what the crew declares: its goal, the run's
    /// variables, each task by number with its description, expected output, dependencies and agent
    /// (GAP-31). <see cref="DomainExecutionPlan.Empty"/> when the crew does not plan.
    /// </summary>
    /// <remarks>
    /// The plan is advice (decision 2.5): a reply the planner cannot read twice, a task it leaves
    /// out, an entry it cannot use are warnings, and the crew runs on; only a provider that fails —
    /// a refused key, an unreachable endpoint — fails the run, before its first task, saying why. A
    /// provider that replays its prompt (the echo provider of a host without an <c>Llm</c> section)
    /// is not asked: there is nothing it could plan (decision 4). The call takes the run's token.
    /// </remarks>
    private async System.Threading.Tasks.Task<DomainExecutionPlan> PlanIfAskedAsync(
        Orkeon.Domain.Crew.Crew crew,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        if (!crew.Planning)
            return DomainExecutionPlan.Empty;

        var crewName = crew.Name ?? crew.Id.ToString();
        var planningLlm = crew.PlanningLlm ?? DefaultPlanningLlm(crew);
        if (planningLlm.Capabilities.ReplaysPrompt)
        {
            LogPlanningSkippedOnEcho(crewName);
            return DomainExecutionPlan.Empty;
        }

        var context = await PlanningContextAsync(crew, variables, cancellationToken).ConfigureAwait(false);
        CrewPlanningOutcome planning;
        using (Orkeon.Application.Interfaces.Ports.LlmUsageScope.Begin(
            Orkeon.Application.Interfaces.Ports.LlmUsageOperations.Planning, crewId: crew.Id.ToString()))
        {
            planning = await CrewPlanner.Create(planningLlm, _executionPlanParser)
                .CreatePlanAsync(context, cancellationToken).ConfigureAwait(false);
        }

        foreach (var warning in planning.Warnings)
            LogPlanningWarning(crewName, warning);

        // What was planned, one line per task: the user sees it with --verbose 1.
        for (var index = 0; index < context.Tasks.Count && _logger.IsEnabled(LogLevel.Information); index++)
        {
            if (planning.Plan.InstructionsFor(context.Tasks[index].Id) is not { } instructions)
                continue;

            var task = OneLine(context.Tasks[index].Description, 60);
            var steps = OneLine(instructions, 200);
            LogTaskPlan(crewName, index + 1, task, steps);
        }

        return planning.Plan;
    }

    /// <summary>The provider of the host's default profile, the one a crew plans on by default.</summary>
    private Orkeon.Domain.SharedKernel.ILlmProvider DefaultPlanningLlm(Orkeon.Domain.Crew.Crew crew) =>
        _llmProfiles?.Resolve(Orkeon.Application.Interfaces.Ports.LlmProfiles.Default).Provider
        ?? throw new InvalidOperationException(
            $"Crew '{crew.Name ?? crew.Id.ToString()}' asks for planning, but it names no planning provider and this " +
            "orchestrator has no LLM profile to plan on: set one with CrewBuilder.WithPlanningLlm, or resolve the " +
            "orchestrator from a container that registers the host's model (AddOrkeonInfrastructure, AddOrkeonLlmProvider).");

    /// <summary>
    /// What the planner is shown of the run (GAP-31): the tasks in the order the run takes them
    /// (<see cref="CrewTaskSequencer"/>), each with its description and expected output interpolated
    /// as its agent reads them (<see cref="TaskTextInterpolation"/>), its dependencies, and the agent it
    /// names with the tools it holds for the task (<see cref="TaskToolbelt"/>: the agent's and the
    /// task's — <c>human_input</c>, which the host adds to a task that asks for it, is not shown); then
    /// the crew's agents that run tasks — the manager of a hierarchical or consensual crew runs none.
    /// </summary>
    private async System.Threading.Tasks.Task<PlanningContext> PlanningContextAsync(
        Orkeon.Domain.Crew.Crew crew,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        if (_taskRepository is null || _agentRepository is null)
        {
            throw new InvalidOperationException(
                $"Crew '{crew.Name ?? crew.Id.ToString()}' asks for planning, but this orchestrator cannot show the planner the " +
                "crew's tasks and agents: resolve it from a container that registers the task and agent repositories " +
                "(AddOrkeonInfrastructure), or pass them to its constructor.");
        }

        var agents = new Dictionary<AgentId, Orkeon.Domain.Agent.Agent>();
        foreach (var agentId in crew.Agents)
        {
            if (await _agentRepository.GetByIdAsync(agentId, cancellationToken).ConfigureAwait(false) is { } agent)
                agents.TryAdd(agentId, agent);
        }

        var tasks = await CrewTaskSequencer.InRunOrderAsync(crew, _taskRepository, cancellationToken).ConfigureAwait(false);
        var sheets = tasks
            .Select(task => new PlanningTask(
                task.Id,
                TaskTextInterpolation.Interpolate(task.Description.Value, variables),
                TaskTextInterpolation.Interpolate(task.ExpectedOutput.Value, variables),
                task.Dependencies,
                task.AssignedAgent is { } named && agents.TryGetValue(named, out var agent)
                    ? Sheet(agent, TaskToolbelt.Compose(agent, task))
                    : null))
            .ToList();

        var managerRunsNoTask = crew.ProcessType == ProcessType.Hierarchical || crew.ProcessType == ProcessType.Consensual;
        var workers = agents.Values
            .Where(agent => !(managerRunsNoTask && agent.Id == crew.ManagerAgentId))
            .Select(agent => Sheet(agent, agent.Tools))
            .ToList();

        return new PlanningContext(crew.Goal.Value, crew.ProcessType, sheets, workers, variables);
    }

    private static PlanningAgent Sheet(Orkeon.Domain.Agent.Agent agent, IReadOnlyList<Orkeon.Domain.Tools.IBaseTool> tools) =>
        new(agent.Role.Value, agent.Goal.Value, [.. tools.Select(tool => tool.Name)]);

    /// <summary>The text on one line, cut to <paramref name="max"/> characters: one log line per task.</summary>
    private static string OneLine(string text, int max)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= max ? line : string.Concat(line.AsSpan(0, max), "…");
    }

    private async System.Threading.Tasks.Task CheckpointTaskOutputsAsync(
        string sessionId,
        DomainCrewOutput domainOutput,
        CancellationToken cancellationToken)
    {
        foreach (var taskOutput in domainOutput.TaskOutputs ?? Enumerable.Empty<Domain.Task.ValueObjects.TaskOutput>())
        {
            await _checkpointManager!.CheckpointAsync(
                sessionId,
                taskOutput.TaskId?.ToString() ?? Guid.NewGuid().ToString(),
                taskOutput.Output,
                cancellationToken).ConfigureAwait(false);
        }
        await _checkpointManager!.CompleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    private static async System.Threading.Tasks.Task<DomainCrewOutput> ExecuteDomainStrategyAsync(
        Orkeon.Domain.Crew.Crew crew,
        IProcessStrategy processStrategy,
        IReadOnlyDictionary<string, string> stringVariables,
        CancellationToken cancellationToken)
    {
        // Every strategy sorts the crew's tasks on their declared dependencies (CrewTaskSequencer),
        // with or without a plan: the plan reaches each task in its prompt and orders nothing
        // (GAP-31). Its order, followed under the dependencies (GAP-29), decided the crew's output,
        // the context a task read and Parallel's round-robin, and a task it cited twice ran twice.
        return crew.ProcessType.Value switch
        {
            "Sequential" => await processStrategy.ExecuteSequentialAsync(crew, stringVariables, cancellationToken).ConfigureAwait(false),
            "Graph" => await processStrategy.ExecuteSequentialAsync(crew, stringVariables, cancellationToken).ConfigureAwait(false),
            // Consensual maps to the sequential entry point of ConsensualProcessStrategy
            // (the strategy runs its voting pipeline over the crew's tasks — R3.3).
            "Consensual" => await processStrategy.ExecuteSequentialAsync(crew, stringVariables, cancellationToken).ConfigureAwait(false),
            "Parallel" => await processStrategy.ExecuteParallelAsync(crew, stringVariables, cancellationToken).ConfigureAwait(false),
            "Hierarchical" => await processStrategy.ExecuteHierarchicalAsync(
                crew,
                crew.ManagerAgentId ?? throw new InvalidOperationException("Hierarchical process requires a manager agent"),
                stringVariables,
                cancellationToken).ConfigureAwait(false),
            "Autonomous" => await processStrategy.ExecuteAutonomousAsync(crew, AgentExecutionBudget.Permissive, stringVariables, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Process type {crew.ProcessType} not supported")
        };
    }

    private static TaskOutput ConvertTaskOutput(Domain.Task.ValueObjects.TaskOutput domainTaskOutput)
    {
        return new TaskOutput(
            TaskId: domainTaskOutput.TaskId?.ToString() ?? Guid.NewGuid().ToString(),
            // Real executor agent id when the strategy propagated it; null (not a fabricated
            // "unknown") when genuinely unavailable, to avoid misleading consumers.
            AgentId: domainTaskOutput.AgentId,
            Content: domainTaskOutput.Output ?? string.Empty,
            CompletedAt: domainTaskOutput.GeneratedAt,
            Success: domainTaskOutput.Success,
            ExecutionTime: domainTaskOutput.ExecutionTime,
            ToolsUsed: null);
    }

    /// <summary>
    /// Builds a <see cref="TokenUsage"/> from the real token telemetry carried in the domain
    /// crew metadata (canonical keys on <see cref="Domain.Crew.ValueObjects.CrewMetadata"/>).
    /// Returns null when no token telemetry was collected, so that consumers can distinguish
    /// "not measured" from a genuine zero-cost execution. The prompt/completion split is
    /// honoured when the strategy propagated it; otherwise it stays at 0 with the total
    /// remaining authoritative.
    /// </summary>
    private static TokenUsage? ExtractTokenUsage(DomainCrewOutput domainOutput)
    {
        var metadata = domainOutput.Metadata;
        if (!metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey))
            return null;

        var totalTokens = metadata.Get<int>(Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey);
        var promptTokens = metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey)
            ? metadata.Get<int>(Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey)
            : 0;
        var completionTokens = metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey)
            ? metadata.Get<int>(Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey)
            : 0;

        // Cache telemetry (W-08): present only when a provider reported it — a missing
        // pair stays null, never a measured zero.
        long? cacheHit = metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.CacheHitTokensKey)
            ? metadata.Get<long>(Domain.Crew.ValueObjects.CrewMetadata.CacheHitTokensKey)
            : null;
        long? cacheMiss = metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.CacheMissTokensKey)
            ? metadata.Get<long>(Domain.Crew.ValueObjects.CrewMetadata.CacheMissTokensKey)
            : null;

        return new TokenUsage(
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens)
        {
            CacheHitTokens = cacheHit,
            CacheMissTokens = cacheMiss,
        };
    }

    /// <summary>
    /// Runs <paramref name="body"/> with the crew's identity pushed on the hub caller
    /// context. The <see cref="System.Threading.Tasks.Task.Yield"/> is load-bearing: an
    /// <see cref="AsyncLocal{T}"/> mutated in the synchronous prefix of an async method
    /// mutates the *caller's* execution context — the caller of KickoffAsync would keep the
    /// crew's identity ambient after the run, and a batch kickoff would end up wearing the
    /// last crew's badge. Forcing a suspension first forks the context, so the push can only
    /// flow down into the run, never back up.
    /// </summary>
    private async Task<T> RunWithCrewIdentityAsync<T>(CrewId crewId, Func<Task<T>> body)
    {
        if (_hubCallerContext is null)
            return await body().ConfigureAwait(false);

        await System.Threading.Tasks.Task.Yield();

        using var scope = _hubCallerContext.Push(new Orkeon.Application.EventHub.EventHubCaller(crewId, null));
        return await body().ConfigureAwait(false);
    }

    /// <summary>
    /// Kickoff For Each Async.
    /// </summary>
    public async Task<BatchOutput> KickoffForEachAsync(
        CrewId crewId,
        IEnumerable<CrewInput> inputs,
        CancellationToken cancellationToken = default)
    {
        LogOrchestratingBatchExecution(crewId);

        var stopwatch = Stopwatch.StartNew();

        // Mandatory parallelism
        var tasks = inputs.Select(input => KickoffAsync(crewId, input, cancellationToken));
        var results = await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false);

        stopwatch.Stop();

        return new BatchOutput(
            Results: results.ToList(),
            SuccessCount: results.Count(r => r.Succeeded),
            FailureCount: results.Count(r => !r.Succeeded),
            TotalDuration: stopwatch.Elapsed);
    }

    /// <summary>
    /// Kickoff Async No Wait.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Background execution fault barrier: any failure in the detached run is logged and recorded on the tracked execution state (Failed) so it cannot fault the background task or crash the host.")]
    public async Task<CrewExecutionId> KickoffAsyncNoWait(
        CrewId crewId,
        CrewInput input)
    {
        LogOrchestratingAsyncExecution(crewId);

        var executionId = ExecutionId.New();

        // Create state through the manager under the real execution id (and with the
        // input) so that the status updates below target the tracked state and durable
        // persistence/resume (R3.8) has a coherent identifier. Previously the manager
        // generated its own id, orphaning the state and breaking every update.
        _ = await _stateManager.CreateStateAsync(crewId, executionId, input).ConfigureAwait(false);

        // Start background execution - delegate to regular kickoff
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _stateManager.UpdateStateAsync(executionId, s =>
                {
                    s.Status = ExecutionState.Running;
                }).ConfigureAwait(false);

                var output = await KickoffAsync(crewId, input).ConfigureAwait(false);

                await _stateManager.UpdateStateAsync(executionId, s =>
                {
                    // KickoffAsync never throws; Succeeded is how a failed run says so, and
                    // marking it Completed regardless would make the no-wait state lie.
                    s.Output = output;
                    s.Status = output.Succeeded ? ExecutionState.Completed : ExecutionState.Failed;
                    s.Error = output.Succeeded ? s.Error : output.FinalOutput;
                    s.Progress = 1.0;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogAsyncCrewOrchestrationError(ex);

                await _stateManager.UpdateStateAsync(executionId, s =>
                {
                    s.Error = ex.Message;
                    s.Status = ExecutionState.Failed;
                    s.Progress = 1.0;
                }).ConfigureAwait(false);
            }
            finally
            {
                await _stateManager.CompleteExecutionAsync(executionId).ConfigureAwait(false);
            }
        });

        return CrewExecutionId.From(executionId.Value);
    }

    /// <summary>
    /// Kickoff Streaming Async.
    /// </summary>
    public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(
        CrewId crewId,
        CrewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        ArgumentNullException.ThrowIfNull(input);
        return KickoffStreamingCoreAsync(cancellationToken);

        // [EnumeratorCancellation] belongs on the actual async-iterator (this local function), so a
        // consumer's WithCancellation(token) still flows into the stream after the S4457 split.
        async IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingCoreAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // If streaming service is available, use real streaming; otherwise fall back
            // to a normal execution whose task outputs are replayed as events. The fallback
            // silently loses tool-call granularity, so we warn loudly (once per kickoff)
            // naming the missing registration.
            var canStreamGranularly = _streamingService != null && _agentRepository != null;
            if (!canStreamGranularly)
                LogStreamingDegraded();

            var events = canStreamGranularly
                ? StreamViaServiceAsync(crewId, input, cancellationToken)
                : StreamViaFallbackAsync(crewId, input, cancellationToken);

            await foreach (var ev in events.ConfigureAwait(false))
                yield return ev;
        }
    }

    private async IAsyncEnumerable<CrewExecutionEvent> StreamViaServiceAsync(
        CrewId crewId,
        CrewInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var crew = await _crewRepository.GetByIdAsync(crewId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Crew {crewId.ToString()} not found");

        // The streamed run is not wired to the crew's memory (GAP-30): it recalls nothing and
        // stores nothing — said once, rather than a crew with memory: true silently forgetting.
        if (crew.MemoryEnabled)
            LogStreamingIgnoresMemory(crew.Name ?? crew.Id.ToString());

        // Nor to its plan (GAP-31): a streamed run does not plan — said once, like the memory.
        if (crew.Planning)
            LogStreamingIgnoresPlanning(crew.Name ?? crew.Id.ToString());

        try
        {
            await foreach (var ev in StreamCrewTasksAsync(crew, input, cancellationToken).ConfigureAwait(false))
                yield return ev;
        }
        finally
        {
            await DispatchCrewEventsAsync(crew).ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<CrewExecutionEvent> StreamCrewTasksAsync(
        Orkeon.Domain.Crew.Crew crew,
        CrewInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var crewId = crew.Id;
        var agents = await LoadAgentsAsync(crew, cancellationToken).ConfigureAwait(false);

        if (agents.Count == 0)
        {
            yield return new CrewExecutionEvent("unknown", "error",
                new AgentThought("No agents found for crew", AgentThought.ThoughtType.Error, null, DateTime.UtcNow),
                DateTime.UtcNow);
            yield break;
        }

        // Same identity discipline as KickoffAsync — the streaming path used to skip the
        // push entirely, leaving CrewId.System ambient, which quietly disabled the
        // receive_message ownership guard for streamed agents. Yield first: the fork keeps
        // the push inside this iterator's flow (see RunWithCrewIdentityAsync).
        await System.Threading.Tasks.Task.Yield();
        using var identityScope = _hubCallerContext?.Push(
            new Orkeon.Application.EventHub.EventHubCaller(crewId, null));

        var context = new Orkeon.Application.Context.SimpleExecutionContext(
            crewId,
            new Dictionary<string, string>(PromptVariables(input)),
            Orkeon.Application.Context.NullMemoryScope.Instance,
            []);
        var agentIndex = 0;

        foreach (var taskId in crew.Tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var agent = agents[agentIndex % agents.Count];
            agentIndex++;

            // Create a domain task for streaming
            var domainTask = new CrewTaskBuilder()
                .Description(taskId.ToString())
                .ExpectedOutput("Complete the assigned task")
                .Build();

            await foreach (var thought in _streamingService!.StreamExecutionAsync(
                agent, domainTask, context, cancellationToken).ConfigureAwait(false))
            {
                yield return new CrewExecutionEvent(
                    AgentRole: agent.Role.ToString(),
                    TaskDescription: domainTask.Description,
                    Thought: thought,
                    Timestamp: thought.Timestamp);
            }
        }
    }

    private async System.Threading.Tasks.Task<List<Domain.Agent.Agent>> LoadAgentsAsync(
        Orkeon.Domain.Crew.Crew crew,
        CancellationToken cancellationToken)
    {
        var agents = new List<Domain.Agent.Agent>();
        foreach (var agentId in crew.Agents)
        {
            var agent = await _agentRepository!.GetByIdAsync(agentId, cancellationToken).ConfigureAwait(false);
            if (agent != null)
                agents.Add(agent);
        }
        return agents;
    }

    private async IAsyncEnumerable<CrewExecutionEvent> StreamViaFallbackAsync(
        CrewId crewId,
        CrewInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Fallback: execute normally and emit events for each task output
        var result = await KickoffAsync(crewId, input, cancellationToken).ConfigureAwait(false);

        foreach (var taskOutput in result.TaskOutputs)
        {
            yield return new CrewExecutionEvent(
                AgentRole: taskOutput.AgentId ?? "unknown",
                TaskDescription: taskOutput.TaskId ?? "unknown",
                Thought: new AgentThought(
                    taskOutput.Content,
                    AgentThought.ThoughtType.Conclusion,
                    null,
                    taskOutput.CompletedAt),
                Timestamp: taskOutput.CompletedAt);
        }
    }

    /// <summary>
    /// Get Execution Status Async.
    /// </summary>
    public Task<CrewExecutionStatus> GetExecutionStatusAsync(
        CrewExecutionId executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        return GetExecutionStatusCoreAsync();

        async Task<CrewExecutionStatus> GetExecutionStatusCoreAsync()
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogStatusCheck(executionId);

            var state = await _stateManager.GetStateAsync(ExecutionId.From(executionId.Value), cancellationToken).ConfigureAwait(false);

            if (state == null)
            {
                throw new InvalidOperationException($"Execution {executionId.Value} not found");
            }

            return state.ToStatus();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "KickoffStreamingAsync is degrading to per-task replay — tool-call granularity is lost. No IStreamingAgentExecutionService (and/or IAgentRepository) is registered: call AddOrkeonInfrastructure() (which registers StreamingAgentExecutionService) with an IChatClient/LLM provider configured to stream AgentThought-level events.")]
    private partial void LogStreamingDegraded();
    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}' has memory: true, but a streamed run neither recalls nor stores its memory; KickoffAsync does")]
    private partial void LogStreamingIgnoresMemory(string crew);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}' has planning: true, but a streamed run does not plan; KickoffAsync does")]
    private partial void LogStreamingIgnoresPlanning(string crew);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}': planning skipped — the echo provider cannot plan, it replays its prompt instead of answering it; the crew runs without a plan. Run `orkeon init` to configure a model")]
    private partial void LogPlanningSkippedOnEcho(string crew);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Planning of crew '{Crew}': {Warning}")]
    private partial void LogPlanningWarning(string crew, string warning);
    [LoggerMessage(Level = LogLevel.Information, Message = "Plan of crew '{Crew}' for task {Number} ({Task}): {Plan}")]
    private partial void LogTaskPlan(string crew, int number, string task, string plan);
    [LoggerMessage(Level = LogLevel.Warning, Message = "A handler of domain event {EventName} raised by crew {CrewId} failed; the run's result is unchanged")]
    private partial void LogDomainEventHandlerFailed(Exception ex, string eventName, CrewId crewId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Cannot execute crew: CrewId is null")]
    private partial void LogCrewIdNull();
    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrating crew execution for {CrewId}")]
    private partial void LogOrchestratingCrewExecution(CrewId crewId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Started checkpoint session {SessionId} for crew {CrewId}")]
    private partial void LogCheckpointSessionStarted(string sessionId, CrewId crewId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error orchestrating crew execution")]
    private partial void LogCrewExecutionError(Exception ex);
    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrating batch crew execution for {CrewId}")]
    private partial void LogOrchestratingBatchExecution(CrewId crewId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrating async crew execution for {CrewId}")]
    private partial void LogOrchestratingAsyncExecution(CrewId crewId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error in async crew orchestration")]
    private partial void LogAsyncCrewOrchestrationError(Exception ex);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Orchestrating status check for execution {ExecutionId}")]
    private partial void LogStatusCheck(CrewExecutionId executionId);

    /// <summary>
    /// The variables the prompt composer interpolates and lists under "Context Variables".
    /// The initial context joins them as <c>initial_context</c>: until 2026-09-11 it was
    /// mapped into the domain input and read by nothing -- `--initial-context`, Studio's
    /// field and every programmatic <c>CrewInput.Empty("...")</c> reached no agent. A
    /// caller that already set an <c>initial_context</c> variable keeps its own value.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> PromptVariables(Orkeon.Application.Interfaces.Services.CrewInput input)
    {
        var variables = input.GetStringVariables();
        if (string.IsNullOrWhiteSpace(input.InitialContext) || variables.ContainsKey(InitialContextVariable))
            return variables;
        var merged = new Dictionary<string, string>(variables) { [InitialContextVariable] = input.InitialContext };
        return merged;
    }

    /// <summary>The variable name the initial context is exposed under in prompts and templates.</summary>
    public const string InitialContextVariable = "initial_context";
}
