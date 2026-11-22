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
    private readonly ILogger _verboseLogger;
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

    // The routing warnings of an orchestrator outside a run (#439): in a run, the run's own are said once.
    private readonly Execution.RunWarnings _routeWarnings = new();

    private LlmCallGate LlmGate =>
        _llmGate ??= new LlmCallGate(_logger, _llmProvider, TimeProvider, AgentRequestsPerMinute, _verboseLogger);

    // Wrapped even without callbacks: a streamed run hears each tool call (GAP-32).
    private IToolInvocationPipeline Tools => Orkeon.Application.Execution.StepNotifyingToolInvocationPipeline.Wrap(
        ToolInvocation ?? ToolInvocationPipeline.Unguarded, Callbacks);

    private ChatToolDispatcher ToolDispatcher =>
        _toolDispatcher ??= new ChatToolDispatcher(_logger, Tools, _verboseLogger, ToolLimits);

    private ChatOptionsComposer OptionsComposer =>
        _optionsComposer ??= new ChatOptionsComposer(_logger, _registeredTools, _fileSystem, Tools);

    private ChatClientAgentLoop ChatLoop =>
        _chatLoop ??= new ChatClientAgentLoop(_logger, _chatClient!, LlmGate, OptionsComposer, ToolDispatcher,
            new ChatClientAgentLoop.ChatClientAgentLoopOptions(DeltaSink, _verboseLogger, ToolCallDlp,
                CompactionServices?.For(Interfaces.Ports.LlmProfiles.Default, _fullProvider?.BaseConfig)));

    private LegacyTextAgentLoop LegacyLoop =>
        _legacyLoop ??= new LegacyTextAgentLoop(_logger, _llmProvider, LlmGate, Tools, _registeredTools, _verboseLogger);

    private NativeToolCallingAgentLoop NativeLoop =>
        _nativeLoop ??= new NativeToolCallingAgentLoop(_logger, _fullProvider!, _toolCallingStrategy!, _registeredTools, LlmGate, Tools,
            new NativeToolCallingAgentLoop.NativeToolCallingLoopOptions(_verboseLogger, ToolLimits,
                CompactionServices?.For(Interfaces.Ports.LlmProfiles.Default, _fullProvider?.BaseConfig)));

    /// <summary>
    /// What the loops compact with (#432), or null when <see cref="CompactionOptions"/> or
    /// <see cref="ContextWindows"/> is unset — an orchestrator built by hand —: the loops then trim
    /// their conversation to the last 40 messages, as they did before compaction.
    /// </summary>
    private Compaction.ContextCompactionServices? CompactionServices =>
        CompactionOptions is null || ContextWindows is null
            ? null
            : _compactionServices ??= new Compaction.ContextCompactionServices
            {
                Options = CompactionOptions,
                Windows = ContextWindows,
                Logger = _logger,
                TokenCounter = TokenCounter,
                Observer = CompactionObserver,
                Stages = ContextPressureStages,
                SummaryProvider = () => string.IsNullOrWhiteSpace(CompactionOptions.Profile) || LlmProfiles is null
                    ? null
                    : LlmProfiles.Resolve(CompactionOptions.Profile).Provider,
            };

    private Compaction.ContextCompactionServices? _compactionServices;

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
    /// The tool-result options, whose <c>MaxParallelToolCalls</c> bounds the read calls of one turn that
    /// run together (RT-07). Set by <c>AddOrkeonApplication</c>; null runs the default bound. Read when
    /// the loops are first built.
    /// </summary>
    public Microsoft.Extensions.Options.IOptions<Orkeon.Domain.Tools.ToolResultLimitsOptions>? ToolLimits { get; set; }

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
    /// The DLP interceptor of the external-output channel, when the host registered one (FWK-20): with it, the
    /// arguments of a streamed tool call are held per call and written once, redacted, instead of fragment by
    /// fragment. Set by <c>AddOrkeonApplication</c>; null streams the fragments as they arrive.
    /// </summary>
    public ToolCallDeltaDlp? ToolCallDlp { get; set; }

    /// <summary>
    /// What each crew declared about its memory: a crew with <c>memory: true</c> holds the registered
    /// <c>memory_recall</c> tool when the cognitive memory is wired (FWK-04). Set by
    /// <c>AddOrkeonApplication</c>; null — an orchestrator built by hand — injects none.
    /// </summary>
    public Orkeon.Application.Memory.CrewMemoryProviderRegistry? MemoryRegistry { get; set; }

    /// <summary>
    /// How the native and the chat-client loops compact a long conversation (<c>Orkeon:Compaction</c>,
    /// #432). Set by <c>AddOrkeonApplication</c> with <see cref="ContextWindows"/>; null — an
    /// orchestrator built by hand — keeps the 40-message trim. Read when the loops are first built.
    /// </summary>
    public Compaction.ContextCompactionOptions? CompactionOptions { get; set; }

    /// <summary>The context window a loop compacts against (#432); null keeps the 40-message trim.</summary>
    public IContextWindowResolver? ContextWindows { get; set; }

    /// <summary>The counter that estimates a conversation's prompt (#432); null estimates four characters a token.</summary>
    public Domain.SharedKernel.ITokenCounter? TokenCounter { get; set; }

    /// <summary>Told of every compaction (<c>agent.compacted</c>, #432); null logs it alone.</summary>
    public IAgentCompactionObserver? CompactionObserver { get; set; }

    /// <summary>
    /// The stages that relieve a conversation before compaction does (#432, the seam of #434), in
    /// order; empty by default. Each runs before every model call of a loop that compacts.
    /// </summary>
    public IReadOnlyList<Compaction.IContextPressureStage> ContextPressureStages { get; set; } = [];

    /// <summary>
    /// Reads the skills an agent's prompt carries (<c>skills:</c>, #434); null, an agent's skills are ignored.
    /// </summary>
    internal Skills.AgentSkillsResolver? SkillsResolver { get; set; }

    /// <summary>Told which skills each task's prompt carries (<c>agent.skills</c>, #434); null logs it alone.</summary>
    internal IAgentSkillsObserver? SkillsObserver { get; set; }

    /// <summary>
    /// Picks the profile of each task of an agent on <c>llm.profile: auto</c> (#434); null, such a task runs on
    /// the default profile.
    /// </summary>
    internal Routing.AgentProfileRouter? ProfileRouter { get; set; }

    /// <summary>Told which profile each task of an <c>auto</c> agent runs on (<c>agent.route</c>, #434); null logs it alone.</summary>
    internal IAgentRouteObserver? RouteObserver { get; set; }

    /// <summary>The circuit of this orchestrator's run scope when it is not the container's (#431); null follows the container's.</summary>
    internal Execution.DecisionCircuit? ScopeCircuit { get; set; }

    // What System One picked for each task of an agent (#434) - its profile on llm.profile: auto, its skills -
    // outside a run: within one, the run's own (TaskPickScope), so the next run picks again.
    private readonly Execution.TaskPicks _ownPicks = new();

    private const string RoutePick = "route";
    private const string SkillsPick = "skills";

    private Execution.TaskPicks Picks => Execution.TaskPickScope.Current ?? _ownPicks;

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionOrchestrator"/>.
    /// </summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _verboseLogger = logger;
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
    }

    /// <summary>Constructor of a host without <see cref="IChatClient"/>, taking the logger of category <c>Orkeon.Verbose</c>, which carries the Information turn lines of verbose agents.</summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        ILogger verboseLogger)
        : this(logger, llmProvider)
    {
        ArgumentNullException.ThrowIfNull(verboseLogger);
        _verboseLogger = verboseLogger;
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
        // A tool the container withholds resolves to null (system_one_decide with the decision circuit off,
        // a dropped plugin tool): skipped here, once, for the composer and both loops (#448). Lazy, so a
        // live collection is still read at each composition.
        _registeredTools = registeredTools?.Where(static tool => tool is not null);
    }

    /// <summary>Constructor taking the logger of category <c>Orkeon.Verbose</c>, which carries the Information turn lines of verbose agents.</summary>
    public ExecutionOrchestrator(
        ILogger<ExecutionOrchestrator> logger,
        IBasicLlmProvider llmProvider,
        IChatClient chatClient,
        IEnumerable<Domain.Tools.IBaseTool> registeredTools,
        Domain.FileSystem.IFileSystemService fileSystem,
        ILogger verboseLogger)
        : this(logger, llmProvider, chatClient, registeredTools, fileSystem)
    {
        ArgumentNullException.ThrowIfNull(verboseLogger);
        _verboseLogger = verboseLogger;
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
    /// Constructor with full dependencies and the logger of category <c>Orkeon.Verbose</c>, which carries
    /// the Information turn lines of a verbose agent or of a verbose crew's run (CH-03).
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
        Domain.FileSystem.IFileSystemService fileSystem,
        ILogger verboseLogger)
        : this(logger, llmProvider, chatClient, registeredTools, validationPipeline, parserFactory, fullProvider, toolCallingStrategy, deliverableResolverFactory, fileSystem)
    {
        ArgumentNullException.ThrowIfNull(verboseLogger);
        _verboseLogger = verboseLogger;
    }

    // A call the provider never answered leaves nothing to validate, and a correction
    // round would only ask the same model again — the retries belong to the provider
    // layer (LLM-11). Every other exit keeps its validation pass.
    private async System.Threading.Tasks.Task<(string Output, object? Structured)> ValidateLoopOutputAsync(
        AgentLoopResult loopResult, OutputValidationRequest request, CancellationToken cancellationToken)
    {
        if (loopResult.ExitReason == AgentExitReason.LlmCallFailed)
        {
            return (loopResult.Output, null);
        }

        return await OutputValidation.ValidateAndParseOutputAsync(request, MaxOutputRetries, cancellationToken).ConfigureAwait(false);
    }

    private static TaskResult CompletedResult(
        AgentLoopResult loopResult,
        string finalOutput,
        object? structuredOutput,
        List<Domain.Tools.ToolUsage> toolsUsed,
        TimeSpan executionTime,
        DeliverableFqnOutcome fqnOutcome)
    {
        // Success is the exit reason, and a failed exit names its reason in Error —
        // the strategies, AUTO_SUMMARY and the runner read Error, and it used to stay
        // null on every non-Completed exit ("Task failed: unknown error").
        var succeeded = loopResult.ExitReason == AgentExitReason.Completed;
        return new TaskResult(
            Success: succeeded,
            Output: finalOutput,
            StructuredOutput: structuredOutput,
            ToolsUsed: toolsUsed,
            ExecutionTime: executionTime,
            Error: succeeded
                ? null
                : loopResult.LastError ?? $"Agent exited with reason {loopResult.ExitReason}",
            TokensUsed: loopResult.TokensUsed)
        {
            ExitReason = loopResult.ExitReason,
            IterationsUsed = loopResult.IterationsUsed,
            EndedOnIterationCap = loopResult.EndedOnIterationCap,
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task '{TaskName}' (agent {AgentRole}) completed without writing its deliverable '{Path}': the file does not exist")]
    private static partial void LogDeliverableMissing(ILogger logger, string taskName, object agentRole, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task '{TaskName}' (agent {AgentRole}) completed without writing its deliverable '{Path}': the file is the one that was there before the task, unchanged")]
    private static partial void LogDeliverableUnchanged(ILogger logger, string taskName, object agentRole, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Task '{TaskName}': could not check whether its deliverable '{Path}' was written")]
    private static partial void LogDeliverableProbeFailed(ILogger logger, Exception exception, string taskName, string path);

    /// <summary>
    /// What a <c>tool_call</c> deliverable's file was at one point of an attempt: whether it exists and,
    /// when the file system gives them, its metadata (size, last write). <see cref="Unknown"/> when it
    /// could not be read.
    /// </summary>
    private sealed record DeliverableFileState(bool Exists, Orkeon.Domain.FileSystem.VirtualFileEntry? Entry)
    {
        public static DeliverableFileState Unknown { get; } = new(false, null);

        /// <summary>Whether the file could not be read: nothing is concluded from it.</summary>
        public bool IsUnknown => ReferenceEquals(this, Unknown);

        /// <summary>
        /// Whether <paramref name="after"/> is this very file, untouched: same size, same last write.
        /// Without metadata on either side nothing tells it apart, and the existence alone decides.
        /// </summary>
        public bool IsUnchangedIn(DeliverableFileState after) =>
            Exists && after.Exists
            && Entry is { } before && after.Entry is { } now
            && before.SizeBytes == now.SizeBytes && before.LastModified == now.LastModified;
    }

    /// <summary>
    /// The state of the task's <c>tool_call</c> deliverable as its first attempt of the run begins (#392,
    /// read once per run through <see cref="DeliverableBaselineScope"/>), so that the end of an attempt
    /// tells a file the task wrote from one an earlier run left. Null when the task has no such
    /// deliverable (or no file system to read). A cancellation is never swallowed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort probe: a failure to check the deliverable never fails the task.")]
    private async System.Threading.Tasks.Task<DeliverableFileState?> ReadDeliverableBaselineAsync(CrewTask task, CancellationToken cancellationToken)
    {
        if (task.Deliverable is not { Source: DeliverableSource.ToolCall, Path.Length: > 0 } deliverable || _fileSystem is null)
            return null;

        var taskName = DeliverableToolCheck.NameOf(task);
        try
        {
            return await ReadDeliverableFileAsync(_fileSystem, deliverable.Path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDeliverableProbeFailed(_logger, ex, taskName, deliverable.Path);
            return DeliverableFileState.Unknown;
        }
    }

    // The entry's metadata when the file system gives it; otherwise the existence alone.
    private static async System.Threading.Tasks.Task<DeliverableFileState> ReadDeliverableFileAsync(
        Orkeon.Domain.FileSystem.IFileSystemService fileSystem, string path, CancellationToken cancellationToken)
    {
        if (await fileSystem.TryGetEntryAsync(path, cancellationToken).ConfigureAwait(false) is { } entry)
            return new DeliverableFileState(entry.Kind != Orkeon.Domain.FileSystem.VirtualEntryKind.Directory, entry);

        return new DeliverableFileState(await fileSystem.ExistsAsync(path, cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// What the task's deliverable became once the task succeeded (#392): a <c>tool_call</c>
    /// deliverable the attempt did not write - absent at its end, or present but unchanged since its
    /// start (<paramref name="baseline"/>) - marks the result <see cref="TaskResult.DeliverableMissing"/>
    /// and logs a warning; a deliverable written during the attempt, or persisted by a resolver, is
    /// named in <see cref="TaskResult.DeliverablePath"/>. Best-effort: a probe that fails leaves the
    /// result as it was, it never fails the task; a cancellation is never swallowed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort probe: a failure to check the deliverable never fails a task that succeeded.")]
    private async System.Threading.Tasks.Task<TaskResult> WithDeliverableStateAsync(
        DomainAgent agent, CrewTask task, TaskResult result, string? persistedPath, DeliverableFileState? baseline,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
            return result;

        if (persistedPath is { Length: > 0 })
            return result with { DeliverablePath = persistedPath };

        if (task.Deliverable is not { Source: DeliverableSource.ToolCall, Path.Length: > 0 } deliverable
            || _fileSystem is null || baseline is null || baseline.IsUnknown)
            return result;

        var taskName = DeliverableToolCheck.NameOf(task);
        try
        {
            var after = await ReadDeliverableFileAsync(_fileSystem, deliverable.Path, cancellationToken).ConfigureAwait(false);
            if (!after.Exists)
            {
                LogDeliverableMissing(_logger, taskName, agent.Role, deliverable.Path);
                return result with { DeliverableMissing = true };
            }

            if (baseline.IsUnchangedIn(after))
            {
                LogDeliverableUnchanged(_logger, taskName, agent.Role, deliverable.Path);
                return result with { DeliverableMissing = true };
            }

            return result with { DeliverablePath = deliverable.Path };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDeliverableProbeFailed(_logger, ex, taskName, deliverable.Path);
            return result;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent [{AgentRole}]: the task ran past its maximum execution time of {LimitSeconds}s and is cancelled")]
    private static partial void LogExecutionTimeExceeded(ILogger logger, object agentRole, double limitSeconds);

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
        // The thinking this task keeps across its attempts when it leaves something to System One (#439): told how the
        // attempt ended, so that the next one runs a step higher after a failure.
        Execution.KeptThinking? keptThinking = null;
        return ExecuteTaskStampedAsync();

        // The execution's own end, on the clock the executions use, read before anything after it.
        async System.Threading.Tasks.Task<TaskResult> ExecuteTaskStampedAsync()
        {
            TaskResult result;
            try
            {
                result = await ExecuteTaskCoreInnerAsync().ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // An attempt that throws is a failed one its retry runs a step higher; a cancellation is not (#439).
                keptThinking?.NoteAttempt(true);
                throw;
            }

            var endedAt = TimeProvider.GetUtcNow();
            keptThinking?.NoteAttempt(FailedForRaise(result));
            return result with { EndedAt = endedAt };
        }

        async System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreInnerAsync()
        {
            var startTime = DateTime.UtcNow;
            var toolsUsed = new List<Domain.Tools.ToolUsage>();

            // Whose calls these are: every LLM call this task makes — its turns, its retries,
            // its correction round, its knowledge retrieval — is metered under this agent,
            // this task, this crew. The scope never leaks past this method.
            using var usageScope = LlmUsageScope.Begin(
                LlmUsageOperations.Agent,
                crewId: context.CrewId.ToString(),
                agentId: agent.Role.Value,
                taskId: task.Id.ToString());

            // The run scope's circuit (#431), when it is not the container's: what this task asks of the decision
            // model through a service built once per host - the judge, a decision profile, the decision reranker -
            // follows it, as the scoped services do.
            using var circuitScope = Execution.DecisionCircuitScope.Begin(ScopeCircuit);

            // The agent's time limit (CH-01): opened before anything waits — the maxRpm turn
            // included — so every wait counts inside it; the one place a task runs in the six modes.
            using var deadline = BeginDeadline(agent, cancellationToken);
            var taskToken = deadline?.Token ?? cancellationToken;

            try
            {
                // What the task runs on — the profile its llm_override names, else its agent's own
                // provider or profile, else the default (GAP-17, GAP-34) —, for its turns and its
                // correction round alike.
                var llm = await LlmForAsync(agent, task, context, taskToken).ConfigureAwait(false);
                // The thinking settled at the task's start when its effort is left to System One (#439): every call of
                // this execution - its turns, its correction round - runs on it.
                // A task that leaves nothing to System One opens a scope too, so that its correction rounds never raise
                // the level of a task it runs inside (a delegated sub-task).
                using var thinkingScope = llm.Kept is { } kept
                    ? Execution.TaskThinkingScope.Begin(new RaisingThinking(this, agent.Role.Value, task.Id.ToString(), kept))
                    : Execution.TaskThinkingScope.Begin(llm.Thinking?.Thinking);
                keptThinking = llm.Kept;
                var toolbelt = TaskToolbelt.Compose(agent, task, _registeredTools, MemoryRegistry?.IsMemoryEnabled(context.CrewId) == true);

                // A provider that runs its own tools — a Microsoft Agent Framework agent — never calls
                // Orkeon's: a task that would hand it some fails before any call, saying what to do
                // (GAP-34). Its prompt therefore never lists a tool.
                if (RefuseOwnTools(agent, task, llm.Provider, toolbelt, toolsUsed, startTime) is { } refused)
                    return refused;

                // The deliverable as the task's first attempt of the run began (#392): the end of this
                // attempt tells a file the task wrote, in this attempt or an earlier one of the run (a
                // retry, a revision, another ballot), from one an earlier run left behind.
                var deliverableBaseline = await DeliverableBaselineScope.FirstAttemptAsync(
                    task.Id, token => ReadDeliverableBaselineAsync(task, token), taskToken).ConfigureAwait(false);

                var systemPrompt = AgentPromptComposer.BuildSystemPrompt(
                    agent, task, toolbelt,
                    _toolCallingStrategy?.SupportsNativeToolCalling == true,
                    _fileSystem);
                // The agent's skills (#434): a fixed list, or the decision model's pick for this task.
                systemPrompt = await AppendSkillsAsync(agent, task, context, systemPrompt, taskToken).ConfigureAwait(false);
                var knowledgeContext = await ResolveKnowledgeContextAsync(
                    agent, task, context, taskToken).ConfigureAwait(false);
                // The task's plan, when the crew plans (GAP-31): the run's scope holds it, so it
                // reaches the task the same way in every mode — a takeover and a revision of the
                // same task included, a ballot or a delegated sub-task (other tasks) not.
                var userPrompt = AgentPromptComposer.BuildUserPrompt(
                    task, context, knowledgeContext, CrewPlanScope.InstructionsFor(task.Id), agent.PromptTemplate, toolbelt);

                // Input phase: the prompt the provider is about to read — previous outputs and
                // retrieved knowledge included — is screened first. A block is a readable task
                // failure; a warning is logged and audited, and the prompt is never rewritten.
                if (await BlockInputAsync(agent, task, context, userPrompt, toolsUsed, startTime, taskToken).ConfigureAwait(false) is { } inputBlocked)
                    return inputBlocked;

                var validationContext = OutputValidationCoordinator.BuildOutputValidationContext(task);

                // While the output phase is guarded, what the turns stream is held until the guardian has
                // judged the output (FWK-19): released when it allows it, dropped when it blocks it.
                using var outputHoldScope = BeginOutputHold(out var outputHold);

                var loopResult = await ExecuteWithProviderAsync(
                    new ExecutionInvocationContext(
                        Agent: agent,
                        Task: task,
                        SystemPrompt: systemPrompt,
                        UserPrompt: userPrompt,
                        Context: context,
                        ToolsUsed: toolsUsed,
                        Stopwatch: new System.Diagnostics.Stopwatch()),
                    llm.Loop,
                    taskToken).ConfigureAwait(false);

                // A call the provider never answered leaves nothing to validate, and a correction
                // round would only ask the same model again — the retries belong to the provider
                // layer (LLM-11). Every other exit keeps its validation pass.
                var (validatedOutput, structuredOutput) = await ValidateLoopOutputAsync(
                    loopResult,
                    new OutputValidationRequest(loopResult.Output, validationContext, task, agent, systemPrompt, userPrompt, toolsUsed)
                    {
                        // A correction round asks the model that answered: the profile's or the
                        // agent's own provider's, when there is one.
                        ChatLoop = llm.Loop,
                    },
                    taskToken).ConfigureAwait(false);

                // Unescape literal \n sequences that LLMs frequently emit in text output
                var finalOutput = ToolCallTextParser.UnescapeLlmText(validatedOutput);

                // Output phase (FWK-19): the validators corrected the form, the guardian judges the
                // policy — before the deliverable is written, the memory kept or task.completed raised.
                // A call the provider never answered has no output to judge. A refusal is a readable
                // task failure, not a provider failure: a profile never fails over on it.
                var outputBlocked = await ApplyOutputPhaseAsync(
                    new OutputPhaseRequest(agent, task, context, loopResult, finalOutput, outputHold, toolsUsed, startTime), taskToken).ConfigureAwait(false);
                if (outputBlocked is not null)
                    return outputBlocked;

                // Framework-managed deliverable persistence (Solution A / B).
                // Legacy tasks with Deliverable == null or Source == ToolCall keep the existing
                // file_write tool-call flow; no resolver is invoked for them.
                var fqnOutcome = await ResolveDeliverableIfDeclaredAsync(task, finalOutput, taskToken).ConfigureAwait(false);

                var completed = CompletedResult(loopResult, finalOutput, structuredOutput, toolsUsed, DateTime.UtcNow - startTime, fqnOutcome);
                return await WithDeliverableStateAsync(agent, task, completed, fqnOutcome.PersistedPath, deliverableBaseline, taskToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ExitedResult("Operation was cancelled", AgentExitReason.Cancelled, toolsUsed, startTime);
            }
            catch (OperationCanceledException) when (HasDeadlineExpired(deadline, cancellationToken))
            {
                var max = agent.MaxExecutionTime!.Value;
                LogExecutionTimeExceeded(_logger, agent.Role, max.TotalSeconds);
                return ExitedResult(
                    $"Agent exceeded its maximum execution time of {max.TotalSeconds}s",
                    AgentExitReason.ExecutionTimeExceeded, toolsUsed, startTime);
            }
            catch (Exception ex) when (BudgetRefusal.Find(ex) is { } spent)
            {
                // The host refused the call before it was sent: the budget does not come back by
                // asking again, so the task ends on that reason and the retry layer stops there.
                return ExitedResult(spent.Message, AgentExitReason.BudgetExhausted, toolsUsed, startTime);
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

    // The agent's time limit (CH-01), when it declares one.
    private TaskDeadline? BeginDeadline(DomainAgent agent, CancellationToken cancellationToken) =>
        agent.MaxExecutionTime is { } limit ? TaskDeadline.Begin(limit, TimeProvider, cancellationToken) : null;

    // A cancellation that is the agent's own time limit running out, not the caller's.
    private static bool HasDeadlineExpired(TaskDeadline? deadline, CancellationToken cancellationToken) =>
        deadline is { HasExpired: true } && !cancellationToken.IsCancellationRequested;

    // Holds what the turns stream while the output phase is guarded (FWK-19); no hold otherwise.
    private IDisposable? BeginOutputHold(out OutputHold? outputHold)
    {
        outputHold = null;
        return Guardian?.IsPhaseGuarded(GuardPhase.Output) == true ? OutputHold.Begin(out outputHold) : null;
    }

    private TaskResult? RefuseOwnTools(
        DomainAgent agent, CrewTask task, Domain.SharedKernel.ILlmProvider? provider, IReadOnlyList<Domain.Tools.IBaseTool> toolbelt,
        List<Domain.Tools.ToolUsage> toolsUsed, DateTime startTime)
    {
        if (OwnToolsRefusal(agent, task, provider, toolbelt) is not { } refusal)
            return null;

        ExecutionLog.LogOwnToolsRefused(_logger, agent.Role, task.Id, refusal);
        return OwnToolsRefusedResult(refusal, toolsUsed, startTime);
    }

    private async System.Threading.Tasks.Task<TaskResult?> BlockInputAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, string userPrompt,
        List<Domain.Tools.ToolUsage> toolsUsed, DateTime startTime, CancellationToken cancellationToken)
    {
        var inputVerdict = await CheckInputAsync(agent, context, userPrompt, cancellationToken).ConfigureAwait(false);
        if (inputVerdict is not { IsAllowed: false })
            return null;

        ExecutionLog.LogInputBlocked(_logger, agent.Role, task.Id, inputVerdict.Reason ?? "");
        return GuardianBlockedResult(
            $"Blocked by Guardian (input): {inputVerdict.Reason}", 0, toolsUsed, startTime);
    }

    private static TaskResult OwnToolsRefusedResult(
        string refusal, List<Domain.Tools.ToolUsage> toolsUsed, DateTime startTime) =>
        new(
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

    private static TaskResult ExitedResult(
        string error, AgentExitReason exitReason, List<Domain.Tools.ToolUsage> toolsUsed, DateTime startTime) =>
        new(
            Success: false,
            Output: string.Empty,
            StructuredOutput: null,
            ToolsUsed: toolsUsed,
            ExecutionTime: DateTime.UtcNow - startTime,
            Error: error)
        {
            ExitReason = exitReason,
            IterationsUsed = 0
        };

    /// <summary>The inputs of the output phase, grouped to keep its signature short.</summary>
    private sealed record OutputPhaseRequest(
        DomainAgent Agent,
        CrewTask Task,
        SimpleExecutionContext Context,
        AgentLoopResult LoopResult,
        string FinalOutput,
        OutputHold? OutputHold,
        List<Domain.Tools.ToolUsage> ToolsUsed,
        DateTime StartTime);

    /// <summary>
    /// The output phase (FWK-19) on a task's validated output: null when the output may go on (the hold is released),
    /// else the failed result of a refusal (the hold is dropped). A call the provider never answered has no output to
    /// judge, and what it streamed was never judged either: its hold is dropped.
    /// </summary>
    private async System.Threading.Tasks.Task<TaskResult?> ApplyOutputPhaseAsync(
        OutputPhaseRequest request,
        CancellationToken cancellationToken)
    {
        var (agent, task, context, loopResult, finalOutput, outputHold, toolsUsed, startTime) = request;
        if (loopResult.ExitReason == AgentExitReason.LlmCallFailed)
        {
            outputHold?.Drop();
            return null;
        }

        var outputVerdict = await CheckOutputAsync(agent, context, finalOutput, cancellationToken).ConfigureAwait(false);
        if (outputVerdict is { IsAllowed: false })
        {
            outputHold?.Drop();
            ExecutionLog.LogOutputBlocked(_logger, agent.Role, task.Id, outputVerdict.Reason ?? "");
            return GuardianBlockedResult(
                $"Blocked by Guardian (output): {outputVerdict.Reason}", loopResult.IterationsUsed, toolsUsed, startTime);
        }

        outputHold?.Release();
        return null;
    }

    private static TaskResult GuardianBlockedResult(
        string reason, int iterationsUsed, List<Domain.Tools.ToolUsage> toolsUsed, DateTime startTime) =>
        new(
            Success: false,
            Output: string.Empty,
            StructuredOutput: null,
            ToolsUsed: toolsUsed,
            ExecutionTime: DateTime.UtcNow - startTime,
            Error: reason)
        {
            ExitReason = AgentExitReason.GuardianBlocked,
            IterationsUsed = iterationsUsed,
            LastError = reason,
        };

    /// <summary>
    /// Runs the guardian's input phase on the composed user prompt; null when no guardian is set.
    /// </summary>
    private async System.Threading.Tasks.Task<GuardResult?> CheckInputAsync(
        DomainAgent agent,
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
    /// Runs the guardian's output phase on a task's validated output; null when no guardian is set.
    /// </summary>
    private async System.Threading.Tasks.Task<GuardResult?> CheckOutputAsync(
        DomainAgent agent,
        SimpleExecutionContext context,
        string output,
        CancellationToken cancellationToken)
    {
        if (Guardian is null)
            return null;

        return await Guardian.ExecuteAsync(new GuardContext
        {
            Phase = GuardPhase.Output,
            AgentId = agent.Id.ToString(),
            AgentRole = agent.Role.Value,
            CrewId = context.CrewId.ToString(),
            Content = output,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends the agent's skills block (#434) to <paramref name="systemPrompt"/>. The block is chosen at the task's
    /// first attempt by this agent in the run, the observer told then, and every later attempt carries the same.
    /// The prompt is unchanged when no resolver is wired, the agent declares no skill or none is carried (an auto
    /// pick of none, a fallback left empty).
    /// </summary>
    private async System.Threading.Tasks.Task<string> AppendSkillsAsync(
        DomainAgent agent,
        CrewTask task,
        SimpleExecutionContext context,
        string systemPrompt,
        CancellationToken cancellationToken)
    {
        if (SkillsResolver is null || agent.Skills is null)
            return systemPrompt;

        var resolver = SkillsResolver;
        var taskId = task.Id.ToString();
        var resolution = await Picks.OnceAsync(SkillsPick, taskId, agent.Id.ToString(), async token =>
        {
            var chosen = await resolver.ResolveAsync(
                agent, taskId, AgentPromptComposer.BuildKnowledgeQueryText(task, context), ScopeCircuit, token).ConfigureAwait(false);
            if (chosen is not null)
                SkillsObserver?.SkillsChosen(chosen.Notice);
            return chosen;
        }, cancellationToken).ConfigureAwait(false);
        if (resolution is null)
            return systemPrompt;

        return resolution.Block.Length == 0
            ? systemPrompt
            : systemPrompt + Environment.NewLine + resolution.Block + Environment.NewLine;
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
        ExecutionInvocationContext invocation,
        ChatClientAgentLoop? profileLoop,
        CancellationToken cancellationToken)
    {
        var (agent, task, systemPrompt, userPrompt, _, toolsUsed, sw) = invocation;
        var turnLogger = ExecutionLog.TurnLogger(_logger, _verboseLogger, agent);
        ExecutionLog.LogLlmRequestStart(turnLogger, agent.Role, task.Id, _chatClient != null ? "IChatClient" : "IBasicLlmProvider");
        ExecutionLog.LogLlmSystemPrompt(_logger, agent.Role, systemPrompt);
        ExecutionLog.LogLlmUserPrompt(_logger, agent.Role, userPrompt);

        sw.Start();

        if (profileLoop is not null)
        {
            var profileResult = await profileLoop.ExecuteAsync(
                agent, task, systemPrompt, userPrompt, toolsUsed, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            ExecutionLog.LogLlmResponse(turnLogger, agent.Role, sw.ElapsedMilliseconds, profileResult.Output);
            return profileResult;
        }

        if (_chatClient != null)
        {
            var loopResult = await ChatLoop.ExecuteAsync(
                agent, task, systemPrompt, userPrompt, toolsUsed, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            ExecutionLog.LogLlmResponse(turnLogger, agent.Role, sw.ElapsedMilliseconds, loopResult.Output);
            return loopResult;
        }

        // Fallback: multi-turn with legacy IBasicLlmProvider.
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
    /// default's is unknown to an orchestrator built by hand). <paramref name="Thinking"/> is the thinking its calls
    /// run on when its config leaves the effort to System One (#439), null when it leaves nothing.
    /// </summary>
    private readonly record struct TaskLlm(
        ChatClientAgentLoop? Loop, Domain.SharedKernel.ILlmProvider? Provider, SettledThinking? Thinking = null, Execution.KeptThinking? Kept = null);

    /// <summary>The thinking settled for a task whose effort is left to System One (#439); null for the provider's own.</summary>
    private sealed record SettledThinking(Domain.SharedKernel.ValueObjects.LlmThinkingConfig? Thinking);

    /// <summary>What System One picked for a task, kept by its later attempts (#434, #439).</summary>
    private sealed record RoutedTask(string? Profile, Execution.KeptThinking? Thinking, SettledThinking? HeldOff = null);

    /// <summary>
    /// Whether an attempt failed in a way a raise of the thinking may help (#439): a failed result, as <c>retry:</c>
    /// retries it - never a cancellation, a refusal of the guardian, an exhausted budget or a timeout: a retry that
    /// thinks harder runs longer, and so would time out again.
    /// </summary>
    internal static bool FailedForRaise(TaskResult result) =>
        !result.Success && result.ExitReason is not (AgentExitReason.Cancelled or AgentExitReason.GuardianBlocked
            or AgentExitReason.BudgetExhausted or AgentExitReason.ExecutionTimeExceeded);

    /// <summary>
    /// What <paramref name="task"/> runs on (GAP-17, GAP-34): the profile its <c>llm_override</c>
    /// names — <c>default</c> included —, else its agent's own provider (<c>Agent.Llm</c>), else its
    /// agent's profile, else the host's default. A profile of <c>auto</c> is the one System One picks for the
    /// task (#434), and an effort of <c>auto</c> the level it picks (#439), in one decision call for both.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The profile is not one <see cref="LlmProfiles"/> offers — the crew load checks it first; this is
    /// the guard for a crew built by hand —, or the agent carries its own provider and no registry
    /// can build its client.
    /// </exception>
    private async System.Threading.Tasks.Task<TaskLlm> LlmForAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
    {
        var taskProfile = task.LlmOverride?.Profile;
        var own = string.IsNullOrWhiteSpace(taskProfile) ? agent.Llm : null;
        var profile = string.IsNullOrWhiteSpace(taskProfile) ? agent.LlmConfig?.Profile : taskProfile;
        var routeProfile = own is null && Interfaces.Ports.LlmProfiles.IsAuto(profile);
        var thinking = Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve(
            agent.LlmConfig ?? Domain.SharedKernel.ValueObjects.LlmConfig.OnProfile(), task.LlmOverride).Thinking;
        if (!routeProfile && thinking is not { HasAuto: true })
            return own is not null ? OwnProviderLlm(agent, own) : ProfileLlm(agent, profile);

        var routed = await RoutedTaskAsync(agent, task, context, routeProfile ? null : profile, own, thinking, cancellationToken).ConfigureAwait(false);
        var llm = own is not null ? OwnProviderLlm(agent, own) : ProfileLlm(agent, routed.Profile);
        return llm with { Thinking = routed.Thinking is { } kept ? new SettledThinking(kept.Current) : routed.HeldOff, Kept = routed.Thinking };
    }

    /// <summary>
    /// What System One picks for <paramref name="task"/> of <paramref name="agent"/> (#434, #439), once per agent and
    /// run: the profile of an agent on <c>llm.profile: auto</c>, the thinking of a config on <c>effort: auto</c>,
    /// asked in one call. A later attempt of the same task by the same agent keeps it - its thinking raised one step
    /// when the attempt before failed -, another agent on the task (a voter) has its own picked, and the observer is
    /// told at each pick and each raise. Without a router, the default profile and the effort without <c>auto</c>.
    /// </summary>
    /// <param name="agent">The agent that runs the task.</param>
    /// <param name="task">The task.</param>
    /// <param name="context">The run's context, for the request the decision model reads.</param>
    /// <param name="fixedProfile">The profile when it is not routed (null on <c>auto</c>).</param>
    /// <param name="own">The agent's own provider, when it runs on it.</param>
    /// <param name="thinking">The task's thinking, its override merged.</param>
    /// <param name="cancellationToken">Cancels the decision call.</param>
    private async System.Threading.Tasks.Task<RoutedTask> RoutedTaskAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, string? fixedProfile,
        Domain.SharedKernel.ILlmProvider? own, Domain.SharedKernel.ValueObjects.LlmThinkingConfig? thinking,
        CancellationToken cancellationToken)
    {
        var routeProfile = own is null && fixedProfile is null && Interfaces.Ports.LlmProfiles.IsAuto(
            string.IsNullOrWhiteSpace(task.LlmOverride?.Profile) ? agent.LlmConfig?.Profile : task.LlmOverride.Profile);
        var autoThinking = thinking is { HasAuto: true } ? thinking : null;
        var taskId = task.Id.ToString();
        var fixedProvider = routeProfile ? null : own ?? ProfileLlm(agent, fixedProfile).Provider;

        // A thinking written off below - the agent's, or the profile's when it is not routed - stays off whatever the
        // pick (#439): no level is asked, and the task runs on it switched off.
        SettledThinking? heldOff = null;
        if (autoThinking is not null && (!routeProfile || WrittenStanding(agent, task) is not null)
            && StandingThinking(agent, task, fixedProvider) is var known && autoThinking.IsHeldOffBy(known))
        {
            heldOff = new SettledThinking(autoThinking.Settle(null, known));
            autoThinking = null;
            if (!routeProfile)
                return new RoutedTask(fixedProfile, null, heldOff);
        }

        if (autoThinking is not null)
            EnsureAutoThinkingRunnable(agent, task, autoThinking, fixedProvider);
        if (ProfileRouter is null)
            WarnNoRouter(routeProfile, autoThinking is not null);

        var routed = await Picks.OnceAsync(RoutePick, taskId, agent.Id.ToString(), async token =>
        {
            Routing.TaskRoute? route = null;
            if (ProfileRouter is { } router)
            {
                route = await router.RouteAsync(
                    agent, taskId, AgentPromptComposer.BuildKnowledgeQueryText(task, context), routeProfile, autoThinking, ScopeCircuit, token).ConfigureAwait(false);
                if (route.Profile is { } notice)
                    RouteObserver?.ProfileRouted(notice);
            }

            var profile = routeProfile ? route?.Profile?.Profile ?? Interfaces.Ports.LlmProfiles.Default : fixedProfile;
            if (autoThinking is null)
                return new RoutedTask(profile, null, heldOff);

            var provider = own ?? ProfileLlm(agent, profile).Provider;
            var standing = StandingThinking(agent, task, provider);
            var settled = autoThinking.Settle(route?.Effort?.Level, standing);
            var options = route?.Effort?.Levels
                ?? [.. autoThinking.AutoOptions(ProfileRouter?.EffortLevels ?? Routing.AgentEffortOptions.DefaultLevels)];
            if (route?.Effort is { } pick)
            {
                RouteObserver?.EffortPicked(new AgentEffortNotice
                {
                    TaskId = taskId,
                    AgentRole = agent.Role.Value,
                    Level = Execution.KeptThinking.LevelOf(settled),
                    Reason = pick.Reason,
                    Picked = pick.Picked,
                    Probability = pick.Probability,
                    Levels = pick.Levels,
                });
            }

            return new RoutedTask(profile, new Execution.KeptThinking(autoThinking, standing, settled, options));
        }, cancellationToken).ConfigureAwait(false);

        // The level is kept, except after a failed attempt: one step up, with no decision call (#439).
        if (routed.Thinking is { } kept && kept.RaiseAfterFailure() is { } raise)
            TellRaised(agent.Role.Value, taskId, kept, raise);

        return routed;
    }

    /// <summary>Logs a raise of a task's thinking and tells the observer, <c>agent.effort</c> with <c>reason: raised</c> (#439).</summary>
    private void TellRaised(string agentRole, string taskId, Execution.KeptThinking kept, (string? Previous, string? Raised) raise)
    {
        Routing.AgentRouteLog.EffortRaised(_logger, agentRole, taskId, raise.Previous ?? "(the provider's own)", raise.Raised ?? "(the provider's own)");
        RouteObserver?.EffortPicked(new AgentEffortNotice
        {
            TaskId = taskId,
            AgentRole = agentRole,
            Level = raise.Raised,
            Previous = raise.Previous,
            Reason = AgentEffortReasons.Raised,
            Levels = kept.Options,
        });
    }

    /// <summary>
    /// The thinking one execution of a task runs on when it leaves something to System One (#439): the kept one as the
    /// execution starts, raised a step when a round of its output fails its guardrail. Its own: a concurrent execution of
    /// the same task by the same agent never changes it mid-run.
    /// </summary>
    private sealed class RaisingThinking(ExecutionOrchestrator orchestrator, string agentRole, string taskId, Execution.KeptThinking kept)
        : Execution.ITaskThinking
    {
        private readonly Lock _gate = new();
        private Domain.SharedKernel.ValueObjects.LlmThinkingConfig? _current = kept.Current;

        public Domain.SharedKernel.ValueObjects.LlmThinkingConfig? Current
        {
            get
            {
                lock (_gate)
                    return _current;
            }
        }

        public void RaiseAfterFailedRound()
        {
            (Domain.SharedKernel.ValueObjects.LlmThinkingConfig? Settled, string? Previous, string? Raised)? raise;
            lock (_gate)
            {
                raise = kept.RaiseFrom(_current);
                if (raise is { } raised)
                    _current = raised.Settled;
            }

            if (raise is { } told)
                orchestrator.TellRaised(agentRole, taskId, kept, (told.Previous, told.Raised));
        }
    }

    /// <summary>
    /// The thinking a task on <c>effort: auto</c> would have had without it (#439): the agent's own when the
    /// <c>auto</c> is its task's override and the agent writes a thinking of its own, else the profile's (the
    /// provider's config), else none - the provider's own.
    /// </summary>
    private static Domain.SharedKernel.ValueObjects.LlmThinkingConfig? StandingThinking(
        DomainAgent agent, CrewTask task, Domain.SharedKernel.ILlmProvider? provider) =>
        WrittenStanding(agent, task) ?? provider?.BaseConfig?.Thinking;

    /// <summary>The agent's own thinking when the <c>auto</c> is its task's override and the agent writes one (#439); else null.</summary>
    private static Domain.SharedKernel.ValueObjects.LlmThinkingConfig? WrittenStanding(DomainAgent agent, CrewTask task) =>
        task.LlmOverride?.Thinking is { HasAuto: true } && agent.LlmConfig?.Thinking is { HasAuto: false } written ? written : null;

    /// <summary>
    /// The checks a YAML crew's load makes on a thinking left to System One (#439), made at the start of a task of a
    /// crew built in C# or by hand: bounds <c>min</c>/<c>max</c> that keep none of the host's levels, and a model that
    /// does not think (thinking support <c>None</c>) on a profile that is not routed, fail the task before any call. A
    /// provider that cannot tell is trusted; on <c>llm.profile: auto</c>, the model is the pick's and is not checked.
    /// </summary>
    /// <exception cref="InvalidOperationException">System One would have nothing to pick.</exception>
    private void EnsureAutoThinkingRunnable(
        DomainAgent agent, CrewTask task, Domain.SharedKernel.ValueObjects.LlmThinkingConfig thinking, Domain.SharedKernel.ILlmProvider? provider)
    {
        var where = $"Agent '{agent.Role.Value}' leaves its thinking to System One on task '{task.Key ?? task.Id.ToString()}'";
        IReadOnlyList<string> levels = ProfileRouter?.EffortLevels ?? Routing.AgentEffortOptions.DefaultLevels;
        if (thinking.AutoEffort is { IsBounded: true } bounded && bounded.Allowed(levels).Count == 0)
            throw new InvalidOperationException(
                $"{where} with bounds {{ {bounded.DescribeBounds()} }} that keep none of the levels the host offers ({string.Join(", ", levels)}, " +
                $"{Routing.AgentEffortOptions.LevelsKey}): widen min/max, or add a level inside them to the host's levels.");
        if (provider is null)
            return;

        var model = string.IsNullOrWhiteSpace(task.LlmOverride?.Profile) && !string.IsNullOrWhiteSpace(agent.LlmConfig?.Model)
            ? agent.LlmConfig.Model
            : provider.BaseConfig?.Model;
#pragma warning disable ORKEXP005 // The reasoning capabilities are experimental; read to refuse a pick with no level, as a YAML load does.
        var reasoning = provider.DescribeReasoning(model);
#pragma warning restore ORKEXP005
        if (reasoning is { Thinking: Domain.SharedKernel.ValueObjects.ThinkingSupport.None })
            throw new InvalidOperationException(
                $"{where}, and the model of its profile does not think (thinking support None): System One has nothing to pick. " +
                "Remove the auto, or run it on a profile whose model thinks.");
    }

    /// <summary>
    /// An orchestrator built without a profile router picks nothing (#434, #439): once per run, a warning says so, as the
    /// circuit off does, and the tasks run on the default profile and the effort they would have had without <c>auto</c>.
    /// </summary>
    private void WarnNoRouter(bool routeProfile, bool routeEffort)
    {
        const string reason = "this orchestrator has no profile router (AgentProfileRouter)";
        var warnings = Execution.RunWarnings.Of(_routeWarnings);
        if (routeProfile && warnings.First("route.no-router"))
            Routing.AgentRouteLog.CircuitOff(_logger, reason);
        if (routeEffort && warnings.First("effort.no-router"))
            Routing.AgentRouteLog.EffortCircuitOff(_logger, reason);
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
            new LlmCallGate(_logger, profile.BasicProvider, TimeProvider, AgentRequestsPerMinute, _verboseLogger),
            OptionsComposer,
            ToolDispatcher,
            new ChatClientAgentLoop.ChatClientAgentLoopOptions(DeltaSink, _verboseLogger,
                Compaction: CompactionServices?.For(profile.Name, profile.Provider.BaseConfig)));

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
                    : ImmutableArray<TaskAmbiguousFqn>.Empty)
            {
                PersistedPath = result.Persisted ? result.Path ?? deliv.Path : null,
            };
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
        /// <summary>The path the resolver persisted the deliverable at, or null when it wrote nothing (#392).</summary>
        public string? PersistedPath { get; init; }

        public static DeliverableFqnOutcome Empty { get; } = new(
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableArray<TaskAmbiguousFqn>.Empty);
    }
}
