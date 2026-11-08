using System.Diagnostics;
using System.Globalization;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Telemetry;
using Orkeon.Constants.Llm;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Memory;

/// <summary>
/// The crew's memory as a run uses it (GAP-30): what a crew with <c>memory: true</c> stores after a
/// task and recalls before one, in the long-term memory of the crew — its declared provider, else
/// the host's default one for a named crew, scoped by its name (GAP-20).
/// </summary>
/// <remarks>
/// <para>
/// <b>The switch.</b> Only a crew recorded with <c>memory: true</c>
/// (<see cref="CrewMemoryProviderRegistry.IsMemoryEnabled"/>) remembers. Any other — <c>memory:
/// false</c>, no <c>memory:</c>, a crew never recorded — stores nothing, recalls nothing, and its
/// memory system is never materialized.
/// </para>
/// <para>
/// <b>Vectors.</b> A memory is embedded at write time by the host's <see cref="IEmbeddingProvider"/>
/// — the port of the RAG — on its task and its output, so the same task of an earlier run comes back
/// first. The recall embeds the task the way the knowledge retrieval does
/// (<see cref="AgentPromptComposer.BuildKnowledgeQueryText"/>) and searches by vector within the
/// crew's scope, bounded by <see cref="CrewMemoryOptions"/>, without the memories the prompt
/// already carries as previous outputs.
/// </para>
/// <para>
/// <b>Failures.</b> <see cref="EnsureReadyAsync"/> refuses a memory that cannot work before the
/// run's first LLM call. During the run, a store or a recall that fails is logged as a warning —
/// the crew, the task, the agent, the store, the cause — and the task keeps its output; a
/// cancellation is never swallowed. This class is the only place that rule lives.
/// </para>
/// </remarks>
public partial class MemoryCoordinator : IMemoryCoordinator
{
    /// <summary>The text <see cref="EnsureReadyAsync"/> embeds to probe the embedder and the store.</summary>
    private const string ProbeText = "Is this crew's memory ready?";

    /// <summary>
    /// The most of a task's output its memory is embedded on, after the task: what the task was and
    /// the gist of its result, within what every embedder accepts.
    /// </summary>
    private const int MaxEmbeddedOutputChars = 1_000;

    /// <summary>What ends a memory cut to the recall's budget.</summary>
    private const string TruncationMarker = " … [truncated]";

    /// <summary>The custom property holding when a memory was stored (round-trip UTC).</summary>
    private const string StoredAtProperty = "stored_at";

    private const string AgentRoleProperty = "agent_role";
    private const string TaskDescriptionProperty = "task_description";
    private const float TaskResultImportance = 0.8f;

    private readonly ILogger<MemoryCoordinator> _logger;
    private readonly IMemoryService _memoryService;
    private readonly CrewMemoryProviderRegistry _registry;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly CrewMemoryOptions _options;
    private readonly ICognitiveMemoryService? _cognitive;

    /// <summary>
    /// Initializes a new instance of <see cref="MemoryCoordinator"/>.
    /// </summary>
    /// <param name="logger">The logger; a memory that fails during a run is a warning here.</param>
    /// <param name="memoryService">Where the crew's long-term memory is materialized.</param>
    /// <param name="providerRegistry">What each crew declared at kickoff: whether it remembers, and its scope.</param>
    /// <param name="embeddingProvider">
    /// The host's embedding port, which embeds every memory and every recall query. Without one, a
    /// crew with memory is refused at kickoff.
    /// </param>
    /// <param name="options">The bounds of a recall (<c>Orkeon:CrewMemory</c>); the defaults when null.</param>
    /// <param name="cognitiveMemory">
    /// The cognitive memory (FWK-04), when the host wires it: recall then ranks by composite score and
    /// storing analyses what is stored. Null keeps the vector path.
    /// </param>
    public MemoryCoordinator(
        ILogger<MemoryCoordinator> logger,
        IMemoryService memoryService,
        CrewMemoryProviderRegistry providerRegistry,
        IEmbeddingProvider? embeddingProvider = null,
        IOptions<CrewMemoryOptions>? options = null,
        ICognitiveMemoryService? cognitiveMemory = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(memoryService);
        _memoryService = memoryService;
        ArgumentNullException.ThrowIfNull(providerRegistry);
        _registry = providerRegistry;
        _embeddingProvider = embeddingProvider;
        _options = options?.Value ?? new CrewMemoryOptions();
        _cognitive = cognitiveMemory;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task EnsureReadyAsync(CrewId crewId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return _registry.IsMemoryEnabled(crewId)
            ? EnsureReadyCoreAsync(crewId, cancellationToken)
            : System.Threading.Tasks.Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Readiness barrier: any failure of the embedder or of the store becomes one InvalidOperationException naming the crew, its store and the cause; a cancellation passes.")]
    private async System.Threading.Tasks.Task EnsureReadyCoreAsync(CrewId crewId, CancellationToken cancellationToken)
    {
        var embedder = _embeddingProvider
            ?? throw NotReady(crewId, "no embedding provider is registered", inner: null);

        float[] probe;
        try
        {
            probe = await embedder.GetEmbeddingAsync(ProbeText, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw NotReady(crewId, $"its memories cannot be embedded: {ex.Message}", ex);
        }

        try
        {
            await _memoryService.GetMemorySystem(crewId).LongTerm
                .SearchSimilarAsync(probe, 1, 0f, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw NotReady(crewId, $"its store ({StoreOf(crewId)}) cannot be searched: {ex.Message}", ex);
        }
    }

    /// <summary>The refusal of a crew whose memory cannot work: the crew, the cause, and the remedies.</summary>
    private InvalidOperationException NotReady(CrewId crewId, string cause, Exception? inner) => new(
        $"Crew '{_registry.GetScope(crewId)}' has memory: true, but its memory cannot be used: {cause}. " +
        "A crew's memory embeds what it stores and what it recalls with the host's embedding provider — " +
        "the local model, which RaggableTree:Enabled: false removes, or the Orkeon:Embeddings section — " +
        $"and keeps it in {StoreOf(crewId)}. Fix the embedder or the store, or set memory: false.",
        inner);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);

        if (!_registry.IsMemoryEnabled(context.CrewId) || _options.RecallLimit <= 0 || _options.MaxChars <= 0)
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<RecalledMemory>>([]);

        return RecallCoreAsync(agent, task, context, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Memory fault barrier (GAP-30): a recall that fails is a warning and the task runs without memories; a cancellation passes.")]
    private async System.Threading.Tasks.Task<IReadOnlyList<RecalledMemory>> RecallCoreAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
    {
        var scope = _registry.GetScope(context.CrewId);
        using var span = StartSpan(OrkeonRunSpans.MemoryRecall, "recall", context.CrewId);
        try
        {
            var query = AgentPromptComposer.BuildKnowledgeQueryText(task, context);
            if (_cognitive is not null)
                return await RecallCognitiveAsync(query, scope, task, context, span, cancellationToken).ConfigureAwait(false);

            var embedder = _embeddingProvider
                ?? throw new InvalidOperationException("no embedding provider is registered");
            var embedding = await embedder.GetEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);

            // A memory the prompt already carries — one of this run's previous outputs — is not
            // recalled again: ask for as many more as there are, so the recall still fills its limit.
            var alreadyThere = context.PreviousOutputs
                .Select(output => output.Content)
                .Where(content => !string.IsNullOrEmpty(content))
                .ToHashSet(StringComparer.Ordinal);
            var found = await _memoryService.GetMemorySystem(context.CrewId).LongTerm.SearchSimilarAsync(
                embedding, _options.RecallLimit + context.PreviousOutputs.Count, _options.MinScore, cancellationToken)
                .ConfigureAwait(false);

            // Best first; a content already in the prompt, or already recalled, is skipped.
            var fresh = new List<ScoredMemoryItem>(_options.RecallLimit);
            foreach (var hit in found)
            {
                if (fresh.Count == _options.RecallLimit)
                    break;
                if (alreadyThere.Add(hit.Item.Content))
                    fresh.Add(hit);
            }

            var recalled = Budgeted(fresh.Select(hit => hit.Item));
            span?.SetTag(OrkeonRunSpans.MemoryItemCount, recalled.Count);
            LogMemoriesRecalled(recalled.Count, scope, task.Id);
            return recalled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.Message);
            span?.SetTag(GenAiAttributes.ErrorType, ex.GetType().FullName);
            LogRecallFailed(ex, scope, task.Id, agent.Role.Value, StoreOf(context.CrewId), ex.Message);
            return [];
        }
    }

    /// <summary>
    /// The recall through the cognitive memory: ranked by composite score (similarity, recency,
    /// importance), floored by <see cref="CrewMemoryOptions.MinCompositeScore"/>, bounded like the vector path.
    /// </summary>
    private async System.Threading.Tasks.Task<IReadOnlyList<RecalledMemory>> RecallCognitiveAsync(
        string query, string scope, CrewTask task, SimpleExecutionContext context, Activity? span, CancellationToken cancellationToken)
    {
        var alreadyThere = context.PreviousOutputs
            .Select(output => output.Content)
            .Where(content => !string.IsNullOrEmpty(content))
            .ToHashSet(StringComparer.Ordinal);
        var found = await _cognitive!.RecallAsync(
            context.CrewId,
            query,
            new RecallOptions
            {
                TopK = _options.RecallLimit + context.PreviousOutputs.Count,
                MinScore = _options.MinCompositeScore,
                MinSemanticScore = _options.MinSemanticScore,
            },
            cancellationToken).ConfigureAwait(false);

        var fresh = new List<ScoredMemory>(_options.RecallLimit);
        foreach (var hit in found)
        {
            if (fresh.Count == _options.RecallLimit)
                break;
            if (alreadyThere.Add(hit.Item.Content))
                fresh.Add(hit);
        }

        foreach (var hit in fresh)
            LogCompositeScore(scope, task.Id, hit.CompositeScore, hit.SemanticScore, hit.RecencyScore, hit.ImportanceScore);

        var recalled = Budgeted(fresh.Select(hit => hit.Item));
        span?.SetTag(OrkeonRunSpans.MemoryItemCount, recalled.Count);
        LogMemoriesRecalled(recalled.Count, scope, task.Id);
        return recalled;
    }

    /// <summary>
    /// The hits as recalled memories, within the character budget: whole while they fit, the last
    /// one cut, nothing past it.
    /// </summary>
    private List<RecalledMemory> Budgeted(IEnumerable<MemoryItem> items)
    {
        var recalled = new List<RecalledMemory>();
        var remaining = _options.MaxChars;
        foreach (var item in items)
        {
            if (remaining <= TruncationMarker.Length)
                break;

            var content = item.Content;
            if (content.Length > remaining)
                content = string.Concat(content.AsSpan(0, remaining - TruncationMarker.Length), TruncationMarker);

            recalled.Add(new RecalledMemory(
                StoredAt(item),
                Property(item, AgentRoleProperty),
                TaskOf(item),
                content));
            remaining -= content.Length;
        }

        return recalled;
    }

    private static string Property(MemoryItem item, string name) =>
        item.Metadata.CustomProperties?.GetValueOrDefault(name) ?? string.Empty;

    /// <summary>The task a memory answered: its own record, else the context the cognitive memory kept.</summary>
    private static string TaskOf(MemoryItem item) =>
        Property(item, TaskDescriptionProperty) is { Length: > 0 } description ? description : Property(item, "context");

    /// <summary>When the memory was stored: its own record of it, which every store keeps.</summary>
    private static DateTime StoredAt(MemoryItem item) =>
        DateTime.TryParse(Property(item, StoredAtProperty), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var storedAt)
            ? storedAt
            : item.Timestamp;

    /// <inheritdoc />
    public System.Threading.Tasks.Task StoreTaskResultAsync(
        DomainAgent agent,
        CrewTask task,
        string output,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);

        if (!_registry.IsMemoryEnabled(context.CrewId) || string.IsNullOrWhiteSpace(output))
            return System.Threading.Tasks.Task.CompletedTask;

        return StoreTaskResultCoreAsync(agent, task, output, context, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Memory fault barrier (GAP-30): a store that fails is a warning and the task keeps its output; a cancellation passes.")]
    private async System.Threading.Tasks.Task StoreTaskResultCoreAsync(
        DomainAgent agent, CrewTask task, string output, SimpleExecutionContext context, CancellationToken cancellationToken)
    {
        var scope = _registry.GetScope(context.CrewId);
        using var span = StartSpan(OrkeonRunSpans.MemoryStore, "store", context.CrewId);
        try
        {
            if (_cognitive is not null)
            {
                // The cognitive memory embeds, analyses (importance, category) and stores; an analysis
                // that fails is stored with the default importance (memory.analysis.degraded).
                await _cognitive.RememberAsync(
                    context.CrewId, output, AgentPromptComposer.InterpolateDescription(task, context), cancellationToken)
                    .ConfigureAwait(false);
                span?.SetTag(OrkeonRunSpans.MemoryItemCount, 1);
                LogTaskResultStored(scope, task.Id, agent.Role.Value);
                return;
            }

            var embedder = _embeddingProvider
                ?? throw new InvalidOperationException("no embedding provider is registered");
            var description = AgentPromptComposer.InterpolateDescription(task, context);
            var embedding = await embedder.GetEmbeddingAsync(EmbeddedText(description, output), cancellationToken)
                .ConfigureAwait(false);

            var memory = MemoryItem.Create(
                content: output,
                embedding: embedding,
                importance: TaskResultImportance,
                source: "task_execution",
                tags: [$"agent:{agent.Id}", $"task:{task.Id}", $"crew:{scope}"],
                createdBy: agent.Id,
                customProperties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["agent_id"] = agent.Id.ToString(),
                    [AgentRoleProperty] = agent.Role.Value,
                    ["task_id"] = task.Id.ToString(),
                    [TaskDescriptionProperty] = description,
                    [StoredAtProperty] = Inv.ToString(DateTime.UtcNow, "O"),
                });

            await _memoryService.GetMemorySystem(context.CrewId).LongTerm.AddAsync(memory).ConfigureAwait(false);
            span?.SetTag(OrkeonRunSpans.MemoryItemCount, 1);
            LogTaskResultStored(scope, task.Id, agent.Role.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.Message);
            span?.SetTag(GenAiAttributes.ErrorType, ex.GetType().FullName);
            LogStoreFailed(ex, scope, task.Id, agent.Role.Value, StoreOf(context.CrewId), ex.Message);
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Memory fault barrier (FWK-04): a consolidation that fails is a warning and the run keeps its result; a cancellation passes.")]
    public async System.Threading.Tasks.Task FinishRunAsync(CrewId crewId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        if (_cognitive is null || !_options.ConsolidateOnRunEnd || !_registry.IsMemoryEnabled(crewId))
            return;

        var scope = _registry.GetScope(crewId);
        try
        {
            var result = await _cognitive.ConsolidateAsync(crewId, cancellationToken).ConfigureAwait(false);
            LogConsolidated(scope, result.MergedCount, result.PrunedCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogConsolidationFailed(ex, scope, ex.Message);
        }
    }

    /// <summary>
    /// The text a memory is embedded on: its task, then its output — the start of it, past
    /// <see cref="MaxEmbeddedOutputChars"/>. The content stored is the whole output.
    /// </summary>
    private static string EmbeddedText(string description, string output) =>
        $"{description}\n{(output.Length > MaxEmbeddedOutputChars ? output[..MaxEmbeddedOutputChars] : output)}";

    /// <summary>A memory span under the current task's, tagged with the operation and the provider; no memory text.</summary>
    private Activity? StartSpan(string name, string operation, CrewId crewId)
    {
        var span = OrkeonActivitySources.Memory.StartActivity(name);
        span?.SetTag(OrkeonRunSpans.MemoryOperation, operation);
        span?.SetTag(OrkeonRunSpans.MemoryProvider, _registry.GetProvider(crewId)?.ToString() ?? "default");
        span?.SetTag(OrkeonRunSpans.MemoryItemCount, 0);
        return span;
    }

    /// <summary>Where the crew's memory lives, as a warning or a refusal names it.</summary>
    private string StoreOf(CrewId crewId)
    {
        if (_registry.GetProvider(crewId) is { } type)
            return $"the '{type}' store";

        return _registry.GetName(crewId) is not null
            ? "the host's default store (Memory:Provider)"
            : "an in-process store";
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Crew '{Crew}': recalled {Count} memories for task {TaskId}")]
    private partial void LogMemoriesRecalled(int count, string crew, object taskId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Crew '{Crew}': task {TaskId} recalled a memory at composite {Composite} (similarity {Semantic}, recency {Recency}, importance {Importance})")]
    private partial void LogCompositeScore(string crew, object taskId, float composite, float semantic, float recency, float importance);

    [LoggerMessage(Level = LogLevel.Information, Message = "Crew '{Crew}': memory consolidated at the end of the run (merged {Merged}, pruned {Pruned})")]
    private partial void LogConsolidated(string crew, int merged, int pruned);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}': the memory was not consolidated at the end of the run: {Cause}")]
    private partial void LogConsolidationFailed(Exception ex, string crew, string cause);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Crew '{Crew}': stored the result of task {TaskId} by {Agent}")]
    private partial void LogTaskResultStored(string crew, object taskId, string agent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}': task {TaskId} by {Agent} runs without memories — the recall from {Store} failed: {Cause}")]
    private partial void LogRecallFailed(Exception ex, string crew, object taskId, string agent, string store, string cause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}': the result of task {TaskId} by {Agent} was not stored in {Store}; the task keeps its output: {Cause}")]
    private partial void LogStoreFailed(Exception ex, string crew, object taskId, string agent, string store, string cause);
}
