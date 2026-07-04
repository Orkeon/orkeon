using DomainAgent = Orkeon.Domain.Agent.Agent;
using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Constants.Execution;
using Orkeon.Application.Crew.DeliverableResolvers;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Application.Crew;

/// <summary>
/// Orchestrates task execution for agents with multi-turn iteration loop.
/// Supports native function calling via IChatClient ChatOptions.Tools.
/// </summary>
/// <remarks>
/// Decomposed into focused collaborators (R4.1): this class is now a composition façade
/// that routes execution to <see cref="ChatClientAgentLoop"/>,
/// <see cref="NativeToolCallingAgentLoop"/> or <see cref="LegacyTextAgentLoop"/>, then
/// runs output validation via <see cref="OutputValidationCoordinator"/> and deliverable
/// persistence. Prompt composition lives in <see cref="AgentPromptComposer"/>, text
/// tool-call parsing in <see cref="ToolCallTextParser"/>, conversation/circuit-breaker
/// policy in <see cref="ConversationPolicy"/>. Collaborators are composed internally —
/// the public API (constructors and methods) is unchanged.
/// </remarks>
public partial class ExecutionOrchestrator : IExecutionOrchestrator
{
    private readonly ILogger<ExecutionOrchestrator> _logger;
    private readonly IBasicLlmProvider _llmProvider;
    private readonly IChatClient? _chatClient;
    private readonly Domain.Crew.Planning.IAgentPlanner _planner;
    private readonly IEnumerable<Domain.Tools.IBaseTool>? _registeredTools;
    private readonly IOutputValidationPipeline? _validationPipeline;
    private readonly IOutputParserFactory? _parserFactory;
    private readonly ILlmRateLimiter? _rateLimiter;
    private readonly Domain.SharedKernel.ILlmProvider? _fullProvider;
    private readonly Interfaces.LLM.IToolCallingStrategy? _toolCallingStrategy;
    private readonly IDeliverableResolverFactory? _deliverableResolverFactory;
    private readonly Domain.FileSystem.IFileSystemService _fileSystem = null!;

    // ── Internally composed collaborators (R4.1) ─────────────────────────────
    // Created lazily after construction completes (the telescoping constructors
    // chain, so readonly dependency fields are only all set at the end of the
    // outermost constructor). Collaborators are stateless besides their injected
    // dependencies; mutable knobs (MaxIterations / MaxOutputRetries) are passed
    // per call so live property changes keep their original effect.
    private LlmCallGate? _llmGate;
    private ChatToolDispatcher? _toolDispatcher;
    private ChatOptionsComposer? _optionsComposer;
    private ChatClientAgentLoop? _chatLoop;
    private LegacyTextAgentLoop? _legacyLoop;
    private NativeToolCallingAgentLoop? _nativeLoop;
    private OutputValidationCoordinator? _outputValidation;

    private LlmCallGate LlmGate =>
        _llmGate ??= new LlmCallGate(_logger, _llmProvider, _rateLimiter);

    private ChatToolDispatcher ToolDispatcher =>
        _toolDispatcher ??= new ChatToolDispatcher(_logger);

    private ChatOptionsComposer OptionsComposer =>
        _optionsComposer ??= new ChatOptionsComposer(_logger, _registeredTools, _fileSystem);

    private ChatClientAgentLoop ChatLoop =>
        _chatLoop ??= new ChatClientAgentLoop(_logger, _chatClient!, LlmGate, OptionsComposer, ToolDispatcher);

    private LegacyTextAgentLoop LegacyLoop =>
        _legacyLoop ??= new LegacyTextAgentLoop(_logger, _llmProvider, LlmGate);

    private NativeToolCallingAgentLoop NativeLoop =>
        _nativeLoop ??= new NativeToolCallingAgentLoop(_logger, _fullProvider!, _toolCallingStrategy!, _registeredTools, LlmGate);

    private OutputValidationCoordinator OutputValidation =>
        _outputValidation ??= new OutputValidationCoordinator(
            _logger, _validationPipeline, _parserFactory, _llmProvider,
            _chatClient is null ? null : ChatLoop);

    /// <summary>
    /// Maximum number of output validation retries.
    /// </summary>
    public int MaxOutputRetries { get; set; } = Constants.Orchestration.ValidationDefaults.DefaultMaxOutputRetries;

    /// <summary>
    /// Maximum number of iterations for the agent execution loop.
    /// </summary>
    public int MaxIterations { get; set; } = AgentDefaults.MaxIterations;

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionOrchestrator"/>.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        Domain.Crew.Planning.IAgentPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
        ArgumentNullException.ThrowIfNull(planner);
        _planner = planner;
    }

    /// <summary>
    /// Constructor that accepts IChatClient for the new M.E.AI integration path.
    /// When both are provided, IChatClient is preferred over IBasicLlmProvider.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        Domain.Crew.Planning.IAgentPlanner planner,
        IChatClient chatClient,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, planner)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Constructor with full dependencies including registered tools.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        Domain.Crew.Planning.IAgentPlanner planner,
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, planner, chatClient, fileSystem)
    {
        _registeredTools = registeredTools;
    }

    /// <summary>
    /// Constructor with full dependencies including output validation.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        Domain.Crew.Planning.IAgentPlanner planner,
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        IOutputValidationPipeline validationPipeline,
        IOutputParserFactory parserFactory,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, planner, chatClient, registeredTools, fileSystem)
    {
        _validationPipeline = validationPipeline;
        _parserFactory = parserFactory;
    }

    /// <summary>
    /// Constructor with full dependencies including LLM rate limiter.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        Domain.Crew.Planning.IAgentPlanner planner,
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        IOutputValidationPipeline validationPipeline,
        IOutputParserFactory parserFactory,
        ILlmRateLimiter rateLimiter,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, planner, chatClient, registeredTools, validationPipeline, parserFactory, fileSystem)
    {
        _rateLimiter = rateLimiter;
    }

    /// <summary>
    /// Constructor with full dependencies including native tool calling support.
    /// When <paramref name="fullProvider"/> and <paramref name="toolCallingStrategy"/> are supplied,
    /// the legacy provider path will prefer native (structured) tool calling over text-based [TOOL_CALL] parsing.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        Domain.Crew.Planning.IAgentPlanner planner,
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        IOutputValidationPipeline validationPipeline,
        IOutputParserFactory parserFactory,
        ILlmRateLimiter rateLimiter,
        Domain.SharedKernel.ILlmProvider? fullProvider,
        Interfaces.LLM.IToolCallingStrategy? toolCallingStrategy,
        IDeliverableResolverFactory? deliverableResolverFactory,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, planner, chatClient, registeredTools, validationPipeline, parserFactory, rateLimiter, fileSystem)
    {
        _fullProvider = fullProvider;
        _toolCallingStrategy = toolCallingStrategy;
        _deliverableResolverFactory = deliverableResolverFactory;
    }

    /// <summary>
    /// Execute Task Core Async.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any failure during task execution is converted to a failed TaskResult (cancellation preserved via a separate filtered catch) so one task cannot crash the crew orchestration.")]
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreAsync(
        DomainAgent agent,
        CrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        return ExecuteTaskCoreInnerAsync();

        async System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreInnerAsync()
        {
            var startTime = DateTime.UtcNow;
            var toolsUsed = new List<Domain.Tools.ToolUsage>();

            try
            {
                var systemPrompt = AgentPromptComposer.BuildSystemPrompt(
                    agent, _toolCallingStrategy?.SupportsNativeToolCalling == true);
                var userPrompt = AgentPromptComposer.BuildUserPrompt(task, context);
                var validationContext = OutputValidationCoordinator.BuildOutputValidationContext(task);

                var loopResult = await ExecuteWithProviderAsync(
                    agent, task, systemPrompt, userPrompt, context, toolsUsed, cancellationToken).ConfigureAwait(false);

                var (validatedOutput, structuredOutput) = await OutputValidation.ValidateAndParseOutputAsync(
                    new OutputValidationRequest(loopResult.Output, validationContext, task, agent, systemPrompt, userPrompt, toolsUsed),
                    MaxOutputRetries, MaxIterations, cancellationToken).ConfigureAwait(false);

                // Unescape literal \n sequences that LLMs frequently emit in text output
                var finalOutput = ToolCallTextParser.UnescapeLlmText(validatedOutput);

                // Framework-managed deliverable persistence (Solution A / B).
                // Legacy tasks with Deliverable == null or Source == ToolCall keep the existing
                // file_write tool-call flow; no resolver is invoked for them.
                var fqnOutcome = await ResolveDeliverableIfDeclaredAsync(task, finalOutput, cancellationToken).ConfigureAwait(false);

                return new TaskResult(
                    Success: loopResult.ExitReason == AgentExitReason.Completed,
                    Output: finalOutput,
                    StructuredOutput: structuredOutput,
                    ToolsUsed: toolsUsed,
                    ExecutionTime: DateTime.UtcNow - startTime,
                    TokensUsed: loopResult.TokensUsed)
                {
                    ExitReason = loopResult.ExitReason,
                    IterationsUsed = loopResult.IterationsUsed,
                    LastError = loopResult.LastError,
                    UnknownFqns = fqnOutcome.UnknownFqns,
                    RewrittenFqns = fqnOutcome.RewrittenFqns,
                    AmbiguousFqns = fqnOutcome.AmbiguousFqns,
                    PromptTokens = loopResult.PromptTokens,
                    CompletionTokens = loopResult.CompletionTokens,
                    CacheHitTokens = loopResult.CacheHitTokens,
                    CacheMissTokens = loopResult.CacheMissTokens,
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new TaskResult(
                    Success: false,
                    Output: string.Empty,
                    StructuredOutput: null,
                    ToolsUsed: toolsUsed,
                    ExecutionTime: DateTime.UtcNow - startTime,
                    Error: "Operation was cancelled")
                {
                    ExitReason = AgentExitReason.Cancelled,
                    IterationsUsed = 0
                };
            }
            catch (Exception ex)
            {
                ExecutionLog.LogTaskExecutionError(_logger, ex, task.Id, agent.Id);

                return new TaskResult(
                    Success: false,
                    Output: string.Empty,
                    StructuredOutput: null,
                    ToolsUsed: toolsUsed,
                    ExecutionTime: DateTime.UtcNow - startTime,
                    Error: ex.Message);
            }
        }
    }

    /// <summary>
    /// Routes execution to the appropriate provider: IChatClient (preferred) or legacy IBasicLlmProvider.
    /// The legacy path prefers native (structured) tool calling when a full provider and a
    /// native-capable strategy are available, and falls back to text-based [TOOL_CALL] parsing otherwise.
    /// Rate limit leases are acquired/released per-LLM-call inside the iteration loops,
    /// NOT held across tool execution — this prevents deadlocks when tools (e.g. delegate_work)
    /// trigger nested agent executions that need the same concurrency slot.
    /// Returns an <see cref="AgentLoopResult"/> with output text, token count, and structured exit reason.
    /// </summary>
    private async System.Threading.Tasks.Task<AgentLoopResult> ExecuteWithProviderAsync(
        DomainAgent agent,
        CrewTask task,
        string systemPrompt,
        string userPrompt,
        SimpleExecutionContext context,
        List<Domain.Tools.ToolUsage> toolsUsed,
        CancellationToken cancellationToken)
    {
        ExecutionLog.LogLlmRequestStart(_logger, agent.Role, task.Id, _chatClient != null ? "IChatClient" : "IBasicLlmProvider");
        ExecutionLog.LogLlmSystemPrompt(_logger, agent.Role, systemPrompt);
        ExecutionLog.LogLlmUserPrompt(_logger, agent.Role, userPrompt);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (_chatClient != null)
        {
            var loopResult = await ChatLoop.ExecuteAsync(
                agent, task, systemPrompt, userPrompt, toolsUsed, MaxIterations, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            ExecutionLog.LogLlmResponse(_logger, agent.Role, sw.ElapsedMilliseconds, loopResult.Output.Length, loopResult.Output);
            return loopResult;
        }

        // Fallback: multi-turn with legacy IBasicLlmProvider
        var invocation = new ExecutionInvocationContext(
            Agent: agent,
            Task: task,
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            Context: context,
            ToolsUsed: toolsUsed,
            Stopwatch: sw);

        // Prefer native tool calling when a full provider and strategy are available
        if (_fullProvider != null && _toolCallingStrategy?.SupportsNativeToolCalling == true)
        {
            return await NativeLoop.ExecuteAsync(invocation, MaxIterations, cancellationToken).ConfigureAwait(false);
        }

        // Existing text-based [TOOL_CALL] fallback
        return await LegacyLoop.ExecuteAsync(invocation, MaxIterations, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// When the task carries a <see cref="TaskDeliverable"/> with a source the factory can
    /// dispatch on, run the resolver to persist the deliverable. Failure is logged but never
    /// bubbles up — the task can still complete with a useful text output.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort deliverable persistence: a resolver failure is logged and swallowed so the task can still complete with a useful text output even if the deliverable layer missed.")]
    private async System.Threading.Tasks.Task<DeliverableFqnOutcome> ResolveDeliverableIfDeclaredAsync(
        CrewTask task,
        string finalOutput,
        CancellationToken cancellationToken)
    {
        var deliv = task.Deliverable;
        if (_deliverableResolverFactory is null || deliv is null || deliv.Source == DeliverableSource.None)
            return DeliverableFqnOutcome.Empty;

        var resolver = _deliverableResolverFactory.GetFor(deliv.Source);
        if (resolver is null)
            return DeliverableFqnOutcome.Empty; // legacy ToolCall or unregistered source

        try
        {
            var result = await resolver.ResolveAsync(task, finalOutput, cancellationToken).ConfigureAwait(false);
            if (!result.Persisted)
            {
                ExecutionLog.LogDeliverableNotPersisted(_logger, task.Id.ToString(), deliv.Source.ToString(), deliv.Path, result.FailureReason ?? "unknown");
            }

            return new DeliverableFqnOutcome(
                UnknownFqns: result.UnknownFqns ?? ImmutableArray<string>.Empty,
                RewrittenFqns: result.RewrittenFqns ?? ImmutableDictionary<string, string>.Empty,
                AmbiguousFqns: result.AmbiguousFqns is { } amb
                    ? amb.Select(a => new TaskAmbiguousFqn(a.BareFqn, a.Candidates)).ToImmutableArray()
                    : ImmutableArray<TaskAmbiguousFqn>.Empty);
        }
        catch (Exception ex)
        {
            // Never throw — the task itself may still be a success even if the deliverable layer missed.
            ExecutionLog.LogDeliverableResolverError(_logger, ex, task.Id.ToString(), deliv.Source.ToString(), deliv.Path);
            return DeliverableFqnOutcome.Empty;
        }
    }

    /// <summary>
    /// Aggregated FQN-validation result returned by <see cref="ResolveDeliverableIfDeclaredAsync"/>.
    /// </summary>
    private readonly record struct DeliverableFqnOutcome(
        ImmutableArray<string> UnknownFqns,
        ImmutableDictionary<string, string> RewrittenFqns,
        ImmutableArray<TaskAmbiguousFqn> AmbiguousFqns)
    {
        public static DeliverableFqnOutcome Empty { get; } = new(
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableArray<TaskAmbiguousFqn>.Empty);
    }

    /// <summary>
    /// Plan Execution Async.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Planning fault barrier: any planner failure falls back to a simple single-step plan rather than aborting execution.")]
    public System.Threading.Tasks.Task<TaskExecutionPlan> PlanExecutionAsync(
        DomainAgent agent,
        CrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        return PlanExecutionCoreAsync();

        async System.Threading.Tasks.Task<TaskExecutionPlan> PlanExecutionCoreAsync()
        {
            try
            {
                var plan = await _planner.CreatePlanAsync(task, cancellationToken).ConfigureAwait(false);

                var steps = plan.Steps.Select(s => new PlannedStep(
                    Description: s.Description,
                    ToolName: s.Action,
                    ToolParameters: null // Convert if needed
                )).ToList();

                return new TaskExecutionPlan(
                    AssignedAgent: agent.Id,
                    Steps: steps.AsReadOnly(),
                    EstimatedDuration: TimeSpan.FromMinutes(steps.Count * PlanningDefaults.MinutesPerStepEstimate), // Rough estimate
                    ConfidenceScore: 0.8);
            }
            catch (Exception ex)
            {
                ExecutionLog.LogPlanningError(_logger, ex, task.Id);

                // Return simple fallback plan
                return new TaskExecutionPlan(
                    AssignedAgent: agent.Id,
                    Steps: [new PlannedStep("Execute task directly", null, null)],
                    EstimatedDuration: TimeSpan.FromMinutes(PlanningDefaults.FallbackPlanMinutes),
                    ConfidenceScore: 0.5);
            }
        }
    }

    /// <summary>
    /// Validate Execution Async.
    /// </summary>
    public System.Threading.Tasks.Task<Orkeon.Application.Interfaces.Services.ValidationResult> ValidateExecutionAsync(
        DomainAgent agent,
        CrewTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        // Delegate business rule validation to the domain entity
        var domainResult = agent.ValidateForExecution();
        var issues = new List<string>(domainResult.Issues);

        // Infrastructure-level check (not a business rule)
        if (_llmProvider == null)
        {
            issues.Add("No LLM provider available for agent");
        }

        // Synchronous validation: no awaitable work, so the method does not use
        // async/await and returns a completed task directly.
        return System.Threading.Tasks.Task.FromResult(
            new Orkeon.Application.Interfaces.Services.ValidationResult(
                CanExecute: issues.Count == 0,
                Reason: issues.Count > 0 ? string.Join("; ", issues) : null,
                MissingCapabilities: issues));
    }

    /// <summary>
    /// Map Execution Context.
    /// </summary>
    public Domain.Task.ValueObjects.SimpleTaskExecutionContext MapExecutionContext(SimpleExecutionContext applicationContext, DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(applicationContext);
        ArgumentNullException.ThrowIfNull(agent);
        // Start with existing string variables
        var stringVariables = new Dictionary<string, string>(applicationContext.Variables ?? [])
        {
            // Add agent info
            ["agent_id"] = agent.Id.ToString(),
            ["agent_role"] = agent.Role.Value
        };

        return Domain.Task.ValueObjects.SimpleTaskExecutionContext.Create(
            variables: stringVariables,
            previousOutputs: [],
            memory: null,
            availableAgents: null);
    }
}
