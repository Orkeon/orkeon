using System.Globalization;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
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
    public MemoryCoordinator(
        ILogger<MemoryCoordinator> logger,
        IMemoryService memoryService,
        CrewMemoryProviderRegistry providerRegistry,
        IEmbeddingProvider? embeddingProvider = null,
        IOptions<CrewMemoryOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(memoryService);
        _memoryService = memoryService;
        ArgumentNullException.ThrowIfNull(providerRegistry);
        _registry = providerRegistry;
        _embeddingProvider = embeddingProvider;
        _options = options?.Value ?? new CrewMemoryOptions();
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
        try
        {
            var embedder = _embeddingProvider
                ?? throw new InvalidOperationException("no embedding provider is registered");
            var query = AgentPromptComposer.BuildKnowledgeQueryText(task, context);
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

            var recalled = Budgeted(fresh);
            LogMemoriesRecalled(recalled.Count, scope, task.Id);
            return recalled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRecallFailed(ex, scope, task.Id, agent.Role.Value, StoreOf(context.CrewId), ex.Message);
            return [];
        }
    }

    /// <summary>
    /// The hits as recalled memories, within the character budget: whole while they fit, the last
    /// one cut, nothing past it.
    /// </summary>
    private List<RecalledMemory> Budgeted(IEnumerable<ScoredMemoryItem> hits)
    {
        var recalled = new List<RecalledMemory>();
        var remaining = _options.MaxChars;
        foreach (var item in hits.Select(hit => hit.Item))
        {
            if (remaining <= TruncationMarker.Length)
                break;

            var content = item.Content;
            if (content.Length > remaining)
                content = string.Concat(content.AsSpan(0, remaining - TruncationMarker.Length), TruncationMarker);

            recalled.Add(new RecalledMemory(
                StoredAt(item),
                Property(item, AgentRoleProperty),
                Property(item, TaskDescriptionProperty),
                content));
            remaining -= content.Length;
        }

        return recalled;
    }

    private static string Property(MemoryItem item, string name) =>
        item.Metadata.CustomProperties?.GetValueOrDefault(name) ?? string.Empty;

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
        try
        {
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
            LogTaskResultStored(scope, task.Id, agent.Role.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStoreFailed(ex, scope, task.Id, agent.Role.Value, StoreOf(context.CrewId), ex.Message);
        }
    }

    /// <summary>
    /// The text a memory is embedded on: its task, then its output — the start of it, past
    /// <see cref="MaxEmbeddedOutputChars"/>. The content stored is the whole output.
    /// </summary>
    private static string EmbeddedText(string description, string output) =>
        $"{description}\n{(output.Length > MaxEmbeddedOutputChars ? output[..MaxEmbeddedOutputChars] : output)}";

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Crew '{Crew}': stored the result of task {TaskId} by {Agent}")]
    private partial void LogTaskResultStored(string crew, object taskId, string agent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}': task {TaskId} by {Agent} runs without memories — the recall from {Store} failed: {Cause}")]
    private partial void LogRecallFailed(Exception ex, string crew, object taskId, string agent, string store, string cause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{Crew}': the result of task {TaskId} by {Agent} was not stored in {Store}; the task keeps its output: {Cause}")]
    private partial void LogStoreFailed(Exception ex, string crew, object taskId, string agent, string store, string cause);
}
