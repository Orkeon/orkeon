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
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Interfaces;
// Resolve ambiguous references
using TaskOutput = Orkeon.Application.Execution.TaskOutput;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using DomainCrewInput = Orkeon.Domain.Crew.CrewInput;
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
    private readonly Orkeon.Application.Memory.CrewMemoryProviderRegistry? _memoryProviderRegistry;
    private readonly Orkeon.Application.EventHub.IEventHubCallerContext? _hubCallerContext;

    /// <summary>
    /// Initializes a new instance of <see cref="SequentialCrewOrchestrator"/>.
    /// </summary>
    /// <remarks>Parameters exceed threshold due to DI injection requirements for optional services.</remarks>
#pragma warning disable S107 // Methods should not have too many parameters — DI constructor with optional services
    public SequentialCrewOrchestrator(
        ICrewRepository crewRepository,
        ILogger<SequentialCrewOrchestrator> logger,
        ICrewExecutionStateManager stateManager,
        IProcessStrategyFactory processStrategyFactory,
        IExecutionPlanParser executionPlanParser,
        IStreamingAgentExecutionService? streamingService = null,
        IAgentRepository? agentRepository = null,
        ICheckpointManager? checkpointManager = null,
        Orkeon.Application.Memory.CrewMemoryProviderRegistry? memoryProviderRegistry = null,
        Orkeon.Application.EventHub.IEventHubCallerContext? hubCallerContext = null)
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
        _streamingService = streamingService;
        _agentRepository = agentRepository;
        _checkpointManager = checkpointManager;
        _memoryProviderRegistry = memoryProviderRegistry;
        _hubCallerContext = hubCallerContext;
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
                TokensUsed: null); // nothing executed — nothing was measured
        }

        LogOrchestratingCrewExecution(crewId);

        string? sessionId = null;

        try
        {
            // Load crew from repository
            var crew = await _crewRepository.GetByIdAsync(crewId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Crew {crewId} not found");

            // Record the crew's declared memory provider so the memory subsystem resolves it to a
            // concrete IMemoryProvider for this run (P2-O-02). Idempotent; null clears to host default.
            _memoryProviderRegistry?.SetProvider(crew.Id, crew.MemoryProvider);

            // Start checkpoint session if checkpoint manager is available
            if (_checkpointManager != null)
            {
                sessionId = await _checkpointManager.StartSessionAsync(crewId.ToString(), cancellationToken).ConfigureAwait(false);
                LogCheckpointSessionStarted(sessionId, crewId);
            }

            // Convert application input to domain input
            var domainInput = new DomainCrewInput(
                input.InitialContext ?? "Default context",
                input.Variables?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? []);

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
            using var hubCallerScope = _hubCallerContext?.Push(
                new Orkeon.Application.EventHub.EventHubCaller(crew.Id, null));

            var domainOutput = await ExecuteAndCompleteAsync(
                crew, processStrategy, domainInput, input, cancellationToken).ConfigureAwait(false);

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
                TokensUsed: ExtractTokenUsage(domainOutput));
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
                TokensUsed: null); // failed before telemetry could be collected
        }
        }
    }

    /// <summary>
    /// Runs planning (when enabled), executes the process strategy, and transitions the crew to
    /// its completed state. On any failure the crew is transitioned to failed and the exception
    /// is rethrown so the outer fault barrier can surface it as a failed <see cref="CrewOutput"/>.
    /// </summary>
    private async System.Threading.Tasks.Task<DomainCrewOutput> ExecuteAndCompleteAsync(
        Orkeon.Domain.Crew.Crew crew,
        IProcessStrategy processStrategy,
        DomainCrewInput domainInput,
        CrewInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            // Planning (moved from Crew.KickoffAsync)
            var plan = await CreatePlanIfEnabledAsync(crew, domainInput).ConfigureAwait(false);

            // Extract string variables from input for template interpolation
            var stringVariables = input.GetStringVariables();

            // Execute according to process type (moved from Crew.KickoffAsync)
            var domainOutput = await ExecuteDomainStrategyAsync(
                crew, plan, processStrategy, stringVariables, cancellationToken).ConfigureAwait(false);

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

    private async System.Threading.Tasks.Task<DomainExecutionPlan?> CreatePlanIfEnabledAsync(
        Orkeon.Domain.Crew.Crew crew,
        DomainCrewInput domainInput)
    {
        if (!crew.Planning || crew.PlanningLlm == null)
            return null;

        var planner = CrewPlanner.Create(crew.PlanningLlm, _executionPlanParser);
        var planningContext = new PlanningContext(crew.Id, crew.Goal, crew.Agents);
        return await planner.CreatePlanAsync(planningContext, crew.Tasks, domainInput).ConfigureAwait(false);
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
        DomainExecutionPlan? plan,
        IProcessStrategy processStrategy,
        IReadOnlyDictionary<string, string> stringVariables,
        CancellationToken cancellationToken)
    {
        var defaultPlan = plan ?? DomainExecutionPlan.Create(crew.Tasks);

        return crew.ProcessType.Value switch
        {
            "Sequential" => await processStrategy.ExecuteSequentialAsync(crew, defaultPlan, stringVariables, cancellationToken).ConfigureAwait(false),
            "Graph" => await processStrategy.ExecuteSequentialAsync(crew, defaultPlan, stringVariables, cancellationToken).ConfigureAwait(false),
            // Consensual maps to the sequential entry point of ConsensualProcessStrategy
            // (the strategy runs its voting pipeline over the planned tasks — R3.3).
            "Consensual" => await processStrategy.ExecuteSequentialAsync(crew, defaultPlan, stringVariables, cancellationToken).ConfigureAwait(false),
            "Parallel" => await processStrategy.ExecuteParallelAsync(crew, defaultPlan, stringVariables, cancellationToken).ConfigureAwait(false),
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

        return new TokenUsage(
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens);
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
            SuccessCount: results.Count(r => r.FinalOutput != null),
            FailureCount: results.Count(r => r.FinalOutput == null),
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
                    s.Output = output;
                    s.Status = ExecutionState.Completed;
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

        var agents = await LoadAgentsAsync(crew, cancellationToken).ConfigureAwait(false);

        if (agents.Count == 0)
        {
            yield return new CrewExecutionEvent("unknown", "error",
                new AgentThought("No agents found for crew", AgentThought.ThoughtType.Error, null, DateTime.UtcNow),
                DateTime.UtcNow);
            yield break;
        }

        var context = new Orkeon.Application.Context.SimpleExecutionContext(
            crewId,
            new Dictionary<string, string>(input.GetStringVariables()),
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
}

