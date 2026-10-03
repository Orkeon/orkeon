using DomainAgent = Orkeon.Domain.Agent.Agent;
using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Crew.DeliverableResolvers;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew;

/// <summary>
/// Orchestrates task execution for agents with multi-turn iteration loop.
/// Supports native function calling via IChatClient ChatOptions.Tools.
/// </summary>
/// <remarks>
/// Decomposed into focused collaborators (R4.1): this class is now a composition facade
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
    private readonly IEnumerable<Domain.Tools.IBaseTool>? _registeredTools;
    private readonly IOutputValidationPipeline? _validationPipeline;
    private readonly IOutputParserFactory? _parserFactory;
    private readonly Domain.SharedKernel.ILlmProvider? _fullProvider;
    private readonly Interfaces.LLM.IToolCallingStrategy? _toolCallingStrategy;
    private readonly IDeliverableResolverFactory? _deliverableResolverFactory;
    private readonly Domain.FileSystem.IFileSystemService _fileSystem = null!;

    // ── Internally composed collaborators (R4.1) ─────────────────────────────
    // Created lazily after construction completes (the telescoping constructors
    // chain, so readonly dependency fields are only all set at the end of the
    // outermost constructor). Collaborators are stateless besides their injected
    // dependencies; the mutable MaxOutputRetries is passed per call so a live
    // property change keeps its effect.
    private LlmCallGate? _llmGate;
    private ChatToolDispatcher? _toolDispatcher;
    private ChatOptionsComposer? _optionsComposer;
    private ChatClientAgentLoop? _chatLoop;
    private LegacyTextAgentLoop? _legacyLoop;
    private NativeToolCallingAgentLoop? _nativeLoop;
    private OutputValidationCoordinator? _outputValidation;

    private LlmCallGate LlmGate =>
        _llmGate ??= new LlmCallGate(_logger, _llmProvider, TimeProvider, AgentRequestsPerMinute);

    // Wrapped even without callbacks: a streamed run hears each tool call (GAP-32).
    private IToolInvocationPipeline Tools => Orkeon.Application.Execution.StepNotifyingToolInvocationPipeline.Wrap(
        ToolInvocation ?? ToolInvocationPipeline.Unguarded, Callbacks);

    private ChatToolDispatcher ToolDispatcher =>
        _toolDispatcher ??= new ChatToolDispatcher(_logger, Tools);

    private ChatOptionsComposer OptionsComposer =>
        _optionsComposer ??= new ChatOptionsComposer(_logger, _registeredTools, _fileSystem, Tools);

    private ChatClientAgentLoop ChatLoop =>
        _chatLoop ??= new ChatClientAgentLoop(_logger, _chatClient!, LlmGate, OptionsComposer, ToolDispatcher, DeltaSink);

    private LegacyTextAgentLoop LegacyLoop =>
        _legacyLoop ??= new LegacyTextAgentLoop(_logger, _llmProvider, LlmGate, Tools, _registeredTools);

    private NativeToolCallingAgentLoop NativeLoop =>
        _nativeLoop ??= new NativeToolCallingAgentLoop(_logger, _fullProvider!, _toolCallingStrategy!, _registeredTools, LlmGate, Tools);

    /// <summary>
    /// One agent loop per host profile an agent or a task of this scope named (GAP-17): the
    /// profile's chat client and its own call gate (the <c>gen_ai.provider.name</c> of the spans
    /// follows the profile's provider), sharing the options composer and the tool dispatcher with
    /// the default loop.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChatClientAgentLoop> _profileLoops =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One agent loop per provider an agent of this scope carries itself (<c>Agent.Llm</c>, GAP-34),
    /// keyed by the instance: the client <see cref="LlmProfiles"/> builds over it, with its own call
    /// gate, like a profile's.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Domain.SharedKernel.ILlmProvider, ChatClientAgentLoop> _ownProviderLoops =
        new(ReferenceEqualityComparer.Instance);

    private OutputValidationCoordinator OutputValidation =>
        _outputValidation ??= new OutputValidationCoordinator(
            _logger, _validationPipeline, _parserFactory, _llmProvider,
            _chatClient is null ? null : ChatLoop, LlmGate);

    /// <summary>
    /// Maximum number of output validation retries.
    /// </summary>
    public int MaxOutputRetries { get; set; } = Constants.Orchestration.ValidationDefaults.DefaultMaxOutputRetries;

    /// <summary>
    /// Optional knowledge-context augmenter (RAG-03/C4). When set AND the executing
    /// agent has <see cref="Domain.Knowledge.KnowledgeAttachment"/>s, the attached
    /// collections are queried with the task input and the retrieved excerpts are
    /// injected into the user prompt with numbered citations. Null (the default —
    /// hosts without the RAG subsystem) leaves prompt composition strictly unchanged.
    /// </summary>
    public Orkeon.Rag.Abstractions.Interfaces.IKnowledgeContextAugmenter? KnowledgeAugmenter { get; set; }

    /// <summary>
    /// The clock the agents' and the crews' request windows count on (GAP-38): a request over an
    /// agent's or a crew's <c>maxRpm</c> waits on it. Set by <c>AddOrkeonApplication</c> from the
    /// registered <see cref="System.TimeProvider"/>, else <see cref="TimeProvider.System"/>. Read when
    /// the loops are first built, like <see cref="Callbacks"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// The host's cap on the model requests of each agent per minute
    /// (<c>RateLimiting:AgentRequestsPerMinute</c>, GAP-38): it bounds every agent's window with its own
    /// <c>maxRpm</c>, the stricter winning, and a request over it waits its turn. Set by
    /// <c>AddOrkeonApplication</c> when the host registers the <c>RateLimiting</c> options; null — an
    /// orchestrator built by hand — sets none. Read when the loops are first built.
    /// </summary>
    public int? AgentRequestsPerMinute { get; set; }

    /// <summary>
    /// The guardian whose <see cref="GuardPhase.Input"/> phase screens the composed user prompt
    /// (task, previous outputs, retrieved knowledge) before the first provider call (GAP-09).
    /// Set by <c>AddOrkeonApplication</c> from the registered <see cref="IGuardianPipeline"/>;
    /// null — an orchestrator built by hand — runs no input check.
    /// </summary>
    public IGuardianPipeline? Guardian { get; set; }

    /// <summary>
    /// The single point every tool call of the agent loops goes through: guardian, call,
    /// truncation, result sanitizer, audit (GAP-09). Set by <c>AddOrkeonApplication</c>; null
    /// falls back to <see cref="ToolInvocationPipeline.Unguarded"/>. Read when the loops are
    /// first built, so set it before the first execution.
    /// </summary>
    public IToolInvocationPipeline? ToolInvocation { get; set; }

    /// <summary>
    /// The host's named LLM profiles (GAP-17). An agent whose <c>llm</c> configuration — or a
    /// task whose <c>llm_override</c> — names a profile runs on that profile's provider; an agent
    /// that carries its own provider (<c>Agent.Llm</c>, GAP-34) runs on the client the registry
    /// builds over it (<see cref="ILlmProfileRegistry.ForProvider"/>); every other agent stays on
    /// the orchestrator's own (the host's default profile). Set by <c>AddOrkeonApplication</c>;
    /// null — an orchestrator built by hand — offers the default alone: a task naming another
    /// profile fails with the list of known ones, and a task of an agent carrying its own provider
    /// fails naming the agent.
    /// </summary>
    public ILlmProfileRegistry? LlmProfiles { get; set; }

    /// <summary>
    /// The run's callback orchestrator: every tool call of the agent loops is reported to the
    /// registered <see cref="Callback.ICallbackHandler"/>s as a step, started then completed
    /// (GAP-06). Set by <c>AddOrkeonApplication</c>; null — an orchestrator built by hand —
    /// reports no step. Read when the loops are first built, like <see cref="ToolInvocation"/>.
    /// </summary>
    public Interfaces.Services.ICallbackOrchestrator? Callbacks { get; set; }

    /// <summary>
    /// Where the host renders the model's text as it arrives (<c>orkeon run --stream</c>, the REPL's
    /// console): when set, every turn of the chat-client loop is streamed and each text fragment is
    /// handed to it (GAP-32). Set by <c>AddOrkeonApplication</c> from the registered
    /// <see cref="ILlmDeltaSink"/>; null — the host renders nothing — keeps the turns buffered unless
    /// the run itself is streamed (<c>KickoffStreamingAsync</c>). Read when the loops are first built,
    /// like <see cref="Callbacks"/>. The native and text loops (no <c>IChatClient</c>) stream nothing.
    /// </summary>
    public ILlmDeltaSink? DeltaSink { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionOrchestrator"/>.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
    }

    /// <summary>
    /// Constructor that accepts IChatClient for the new M.E.AI integration path.
    /// When both are provided, IChatClient is preferred over IBasicLlmProvider.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        IChatClient chatClient,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider)
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
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, chatClient, fileSystem)
    {
        _registeredTools = registeredTools;
    }

    /// <summary>
    /// Constructor with full dependencies including output validation.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        IOutputValidationPipeline validationPipeline,
        IOutputParserFactory parserFactory,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, chatClient, registeredTools, fileSystem)
    {
        _validationPipeline = validationPipeline;
        _parserFactory = parserFactory;
    }

    /// <summary>
    /// Constructor with full dependencies including native tool calling support.
    /// When <paramref name="fullProvider"/> and <paramref name="toolCallingStrategy"/> are supplied,
    /// the legacy provider path will prefer native (structured) tool calling over text-based [TOOL_CALL] parsing.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        IOutputValidationPipeline validationPipeline,
        IOutputParserFactory parserFactory,
        Domain.SharedKernel.ILlmProvider? fullProvider,
        Interfaces.LLM.IToolCallingStrategy? toolCallingStrategy,
        IDeliverableResolverFactory? deliverableResolverFactory,
        Domain.FileSystem.IFileSystemService fileSystem)
        : this(logger, llmProvider, chatClient, registeredTools, validationPipeline, parserFactory, fileSystem)
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
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteTaskCoreInnerAsync();

        async System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreInnerAsync()
        {
            var startTime = DateTime.UtcNow;
            var toolsUsed = new List<Domain.Tools.ToolUsage>();

            // Whose calls these are: every LLM call this task makes — its turns, its retries,
            // its correction round, its knowledge retrieval — is metered under this agent,
            // this task, this crew (STUDIO-42). The scope never leaks past this method.
            using var usageScope = LlmUsageScope.Begin(
                LlmUsageOperations.Agent,
                crewId: context.CrewId.ToString(),
                agentId: agent.Role.Value,
                taskId: task.Id.ToString());

            try
            {
                // What the task runs on — the profile its llm_override names, else its agent's own
                // provider or profile, else the default (GAP-17, GAP-34) —, for its turns and its
                // correction round alike.
                var llm = LlmFor(agent, task);
                var toolbelt = TaskToolbelt.Compose(agent, task, _registeredTools);

                // A provider that runs its own tools — a Microsoft Agent Framework agent — never calls
                // Orkeon's: a task that would hand it some fails before any call, saying what to do
                // (GAP-34). Its prompt therefore never lists a tool.
                if (OwnToolsRefusal(agent, task, llm.Provider, toolbelt) is { } refusal)
                {
                    ExecutionLog.LogOwnToolsRefused(_logger, agent.Role, task.Id, refusal);
                    return new TaskResult(
                        Success: false,
                        Output: string.Empty,
                        StructuredOutput: null,
                        ToolsUsed: toolsUsed,
                        ExecutionTime: DateTime.UtcNow - startTime,
                        Error: refusal)
                    {
                        IterationsUsed = 0,
                        LastError = refusal,
                    };
                }

                var systemPrompt = AgentPromptComposer.BuildSystemPrompt(
                    agent, task, toolbelt,
                    _toolCallingStrategy?.SupportsNativeToolCalling == true);
                var knowledgeContext = await ResolveKnowledgeContextAsync(
                    agent, task, context, cancellationToken).ConfigureAwait(false);
                // The task's plan, when the crew plans (GAP-31): the run's scope holds it, so it
                // reaches the task the same way in every mode — a takeover and a revision of the
                // same task included, a ballot or a delegated sub-task (other tasks) not.
                var userPrompt = AgentPromptComposer.BuildUserPrompt(
                    task, context, knowledgeContext, CrewPlanScope.InstructionsFor(task.Id));

                // Input phase: the prompt the provider is about to read — previous outputs and
                // retrieved knowledge included — is screened first. A block is a readable task
                // failure; a warning is logged and audited, and the prompt is never rewritten.
                var inputVerdict = await CheckInputAsync(agent, task, context, userPrompt, cancellationToken).ConfigureAwait(false);
                if (inputVerdict is { IsAllowed: false })
                {
                    var reason = $"Blocked by Guardian (input): {inputVerdict.Reason}";
                    ExecutionLog.LogInputBlocked(_logger, agent.Role, task.Id, inputVerdict.Reason ?? "");
                    return new TaskResult(
                        Success: false,
                        Output: string.Empty,
                        StructuredOutput: null,
                        ToolsUsed: toolsUsed,
                        ExecutionTime: DateTime.UtcNow - startTime,
                        Error: reason)
                    {
                        ExitReason = AgentExitReason.GuardianBlocked,
                        IterationsUsed = 0,
                        LastError = reason,
                    };
                }

                var validationContext = OutputValidationCoordinator.BuildOutputValidationContext(task);

                var loopResult = await ExecuteWithProviderAsync(
                    agent, task, llm.Loop, systemPrompt, userPrompt, context, toolsUsed, cancellationToken).ConfigureAwait(false);

                // A call the provider never answered leaves nothing to validate, and a correction
                // round would only ask the same model again — the retries belong to the provider
                // layer (LLM-11). Every other exit keeps its validation pass.
                var (validatedOutput, structuredOutput) = loopResult.ExitReason == AgentExitReason.LlmCallFailed
                    ? (loopResult.Output, null)
                    : await OutputValidation.ValidateAndParseOutputAsync(
                        new OutputValidationRequest(loopResult.Output, validationContext, task, agent, systemPrompt, userPrompt, toolsUsed)
                        {
                            // A correction round asks the model that answered: the profile's or the
                            // agent's own provider's, when there is one.
                            ChatLoop = llm.Loop,
                        },
                        MaxOutputRetries, cancellationToken).ConfigureAwait(false);

                // Unescape literal \n sequences that LLMs frequently emit in text output
                var finalOutput = ToolCallTextParser.UnescapeLlmText(validatedOutput);

                // Framework-managed deliverable persistence (Solution A / B).
                // Legacy tasks with Deliverable == null or Source == ToolCall keep the existing
                // file_write tool-call flow; no resolver is invoked for them.
                var fqnOutcome = await ResolveDeliverableIfDeclaredAsync(task, finalOutput, cancellationToken).ConfigureAwait(false);

                // Success is the exit reason, and a failed exit names its reason in Error —
                // the strategies, AUTO_SUMMARY and the runner read Error, and it used to stay
                // null on every non-Completed exit ("Task failed: unknown error").
                var succeeded = loopResult.ExitReason == AgentExitReason.Completed;
                return new TaskResult(
                    Success: succeeded,
                    Output: finalOutput,
                    StructuredOutput: structuredOutput,
                    ToolsUsed: toolsUsed,
                    ExecutionTime: DateTime.UtcNow - startTime,
                    Error: succeeded
                        ? null
                        : loopResult.LastError ?? $"Agent exited with reason {loopResult.ExitReason}",
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
    /// Runs the guardian's input phase on the composed user prompt; null when no guardian is set.
    /// </summary>
    private async System.Threading.Tasks.Task<GuardResult?> CheckInputAsync(
        DomainAgent agent,
        CrewTask task,
        SimpleExecutionContext context,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (Guardian is null)
            return null;

        return await Guardian.ExecuteAsync(new GuardContext
        {
            Phase = GuardPhase.Input,
            AgentId = agent.Id.ToString(),
            AgentRole = agent.Role.Value,
            CrewId = context.CrewId.ToString(),
            Content = userPrompt,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the retrieved-knowledge context block for the executing agent (RAG-03/C4).
    /// Returns null — leaving the user prompt byte-identical to the pre-RAG output — when
    /// no <see cref="KnowledgeAugmenter"/> is wired, the agent has no knowledge attachment,
    /// or retrieval keeps no excerpt. Retrieval failures propagate to the task fault barrier
    /// (a failed <see cref="TaskResult"/>) rather than silently degrading to an ungrounded prompt.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> ResolveKnowledgeContextAsync(
        DomainAgent agent,
        CrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (KnowledgeAugmenter is null || agent.KnowledgeAttachments.Count == 0)
            return null;

        var queryText = AgentPromptComposer.BuildKnowledgeQueryText(task, context);
        var block = await KnowledgeAugmenter.BuildContextAsync(
            agent.KnowledgeAttachments, queryText, cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(block?.Text) ? null : block.Text;
    }

    /// <summary>
    /// Routes execution to the appropriate provider: IChatClient (preferred) or legacy IBasicLlmProvider.
    /// The legacy path prefers native (structured) tool calling when a full provider and a
    /// native-capable strategy are available, and falls back to text-based [TOOL_CALL] parsing otherwise.
    /// Each call waits its turn in the agent's and the crew's windows inside the iteration loops
    /// (GAP-38); nothing is held across tool execution, so a tool that runs a nested agent
    /// (delegate_work) never waits on its caller.
    /// Returns an <see cref="AgentLoopResult"/> with output text, token count, and structured exit reason.
    /// </summary>
    private async System.Threading.Tasks.Task<AgentLoopResult> ExecuteWithProviderAsync(
        DomainAgent agent,
        CrewTask task,
        ChatClientAgentLoop? profileLoop,
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

        if (profileLoop is not null)
        {
            var profileResult = await profileLoop.ExecuteAsync(
                agent, task, systemPrompt, userPrompt, toolsUsed, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            ExecutionLog.LogLlmResponse(_logger, agent.Role, sw.ElapsedMilliseconds, profileResult.Output.Length, profileResult.Output);
            return profileResult;
        }

        if (_chatClient != null)
        {
            var loopResult = await ChatLoop.ExecuteAsync(
                agent, task, systemPrompt, userPrompt, toolsUsed, cancellationToken).ConfigureAwait(false);

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
            return await NativeLoop.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
        }

        // Existing text-based [TOOL_CALL] fallback
        return await LegacyLoop.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What a task runs on: the loop of its provider — null for the host's default, which keeps the
    /// orchestrator's own loops — and that provider, whose capabilities the run reads (null when the
    /// default's is unknown to an orchestrator built by hand).
    /// </summary>
    private readonly record struct TaskLlm(ChatClientAgentLoop? Loop, Domain.SharedKernel.ILlmProvider? Provider);

    /// <summary>
    /// What <paramref name="task"/> runs on (GAP-17, GAP-34): the profile its <c>llm_override</c>
    /// names — <c>default</c> included —, else its agent's own provider (<c>Agent.Llm</c>), else its
    /// agent's profile, else the host's default.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The profile is not one <see cref="LlmProfiles"/> offers — the crew load checks it first; this is
    /// the guard for a crew built by hand —, or the agent carries its own provider and no registry
    /// can build its client.
    /// </exception>
    private TaskLlm LlmFor(DomainAgent agent, CrewTask task)
    {
        var taskProfile = task.LlmOverride?.Profile;
        if (string.IsNullOrWhiteSpace(taskProfile) && agent.Llm is { } own)
            return OwnProviderLlm(agent, own);

        return ProfileLlm(agent, string.IsNullOrWhiteSpace(taskProfile) ? agent.LlmConfig?.Profile : taskProfile);
    }

    /// <summary>The loop and provider of the host profile <paramref name="profile"/> — the default's for none.</summary>
    private TaskLlm ProfileLlm(DomainAgent agent, string? profile)
    {
        if (Interfaces.Ports.LlmProfiles.IsDefault(profile))
            return new TaskLlm(null, _fullProvider);

        Interfaces.Ports.LlmProfiles.EnsureKnown(LlmProfiles, profile, $"Agent '{agent.Role.Value}'");
        var resolved = LlmProfiles!.Resolve(profile);
        return new TaskLlm(_profileLoops.GetOrAdd(resolved.Name, _ => LoopOver(resolved)), resolved.Provider);
    }

    /// <summary>
    /// The loop of the provider <paramref name="agent"/> carries itself: the client the host's registry
    /// builds over it, metered there, and one loop per instance in this scope.
    /// </summary>
    private TaskLlm OwnProviderLlm(DomainAgent agent, Domain.SharedKernel.ILlmProvider own)
    {
        if (LlmProfiles is null)
        {
            throw new InvalidOperationException(
                $"Agent '{agent.Role.Value}' runs on its own provider ({own.Name}), and this orchestrator has no LLM " +
                "profile registry to build and meter that provider's client. Resolve the orchestrator from a container " +
                "that registers the host's model (AddOrkeonInfrastructure), or set ExecutionOrchestrator.LlmProfiles.");
        }

        var client = LlmProfiles.ForProvider(own);
        return new TaskLlm(_ownProviderLoops.GetOrAdd(own, _ => LoopOver(client)), client.Provider);
    }

    /// <summary>
    /// A chat-client loop over <paramref name="profile"/>: its chat client and its own call gate (the
    /// <c>gen_ai.provider.name</c> of the spans follows its provider; the agents' and the crews'
    /// windows are the same whatever the provider), sharing the options composer and the tool
    /// dispatcher with the default loop.
    /// </summary>
    private ChatClientAgentLoop LoopOver(LlmProfile profile) =>
        new(
            _logger,
            profile.ChatClient,
            new LlmCallGate(_logger, profile.BasicProvider, TimeProvider, AgentRequestsPerMinute),
            OptionsComposer,
            ToolDispatcher,
            DeltaSink);

    /// <summary>
    /// Why <paramref name="task"/> cannot run on <paramref name="provider"/>, or null (GAP-34): the
    /// provider runs its own tools (<see cref="Domain.SharedKernel.ValueObjects.LlmProviderCapabilities.RunsOwnTools"/>)
    /// and the task's toolbelt — the agent's tools, the task's own, <c>human_input</c> — is not empty.
    /// The agent's own provider is checked when the agent is built; this covers a task's tools, and a
    /// profile or a default that runs its own.
    /// </summary>
    private static string? OwnToolsRefusal(
        DomainAgent agent, CrewTask task, Domain.SharedKernel.ILlmProvider? provider, IReadOnlyList<Domain.Tools.IBaseTool> toolbelt)
    {
        if (provider is not { Capabilities.RunsOwnTools: true } || toolbelt.Count == 0)
            return null;

        var description = task.Description.Value;
        var shortened = description.Length <= 60 ? description : string.Concat(description.AsSpan(0, 57), "...");
        return Domain.Agent.AgentLlmRules.OwnToolsRefusal(
            $"Agent '{agent.Role.Value}', on task '{shortened}',", provider.Name, toolbelt.Select(tool => tool.Name));
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
}
