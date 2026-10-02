using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.Cognitive;

/// <summary>
/// Groups the four cognitive analysis services used by <see cref="CognitiveMemoryService"/>.
/// </summary>
/// <param name="Analyzer">LLM-powered memory analyzer.</param>
/// <param name="ContradictionDetector">LLM-powered contradiction detector.</param>
/// <param name="Consolidator">Memory consolidation service.</param>
/// <param name="Scorer">Composite scoring service.</param>
public sealed record CognitiveAnalysisServices(
    MemoryAnalyzer Analyzer,
    ContradictionDetector ContradictionDetector,
    MemoryConsolidator Consolidator,
    CompositeScorer Scorer);

/// <summary>
/// Cognitive memory service that decorates <see cref="IMemoryService"/> with LLM-powered
/// analysis, contradiction detection, composite scoring, and consolidation.
/// </summary>
/// <remarks>
/// <para>
/// It works in the crew's own memory (GAP-30): the long-term memory <see cref="IMemoryService"/>
/// materializes for the crew — the provider the crew declared, else the host's default one for a
/// named crew, scoped by its name; an in-process store for an unnamed crew. A remembered item is
/// stored there once, stamped <c>kind = crew-memory</c> and <c>crew = &lt;scope&gt;</c> like every
/// memory of the crew's runs (<see cref="Application.Memory.CrewMemoryScope"/>), so a crew has one
/// memory: its runs recall what it remembered, and it recalls what its runs stored.
/// </para>
/// <para>
/// Recall, contradiction candidates and consolidation all search that memory by similarity, within
/// the scope, and a conflict is resolved where its candidates came from. The scope is the one the
/// crew's kickoff recorded: before its first kickoff, a crew id is a crew without a name.
/// </para>
/// </remarks>
public sealed partial class CognitiveMemoryService : ICognitiveMemoryService
{
    /// <summary>
    /// The most memories of one crew a consolidation considers at once — what Pinecone answers at
    /// most, with their metadata, to one query.
    /// </summary>
    private const int ConsolidationScanLimit = 1_000;

    /// <summary>
    /// The text whose vector a consolidation searches from. With no score floor every memory of the
    /// scope comes back, up to <see cref="ConsolidationScanLimit"/>: the probe only orders them.
    /// </summary>
    private const string ConsolidationProbe = "everything this crew remembers";

    private readonly IMemoryService _innerMemoryService;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly MemoryAnalyzer _analyzer;
    private readonly ContradictionDetector _contradictionDetector;
    private readonly MemoryConsolidator _consolidator;
    private readonly CompositeScorer _scorer;
    private readonly CognitiveMemoryOptions _options;
    private readonly ILogger<CognitiveMemoryService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CognitiveMemoryService"/>.
    /// </summary>
    /// <param name="innerMemoryService">The memory service whose crew memories this service works in.</param>
    /// <param name="embeddingProvider">Embeds what is remembered, recalled and compared.</param>
    /// <param name="analysisServices">The LLM analysis services.</param>
    /// <param name="options">The cognitive memory options (<c>Orkeon:CognitiveMemory</c>).</param>
    /// <param name="logger">The logger.</param>
    public CognitiveMemoryService(
        IMemoryService innerMemoryService,
        IEmbeddingProvider embeddingProvider,
        CognitiveAnalysisServices analysisServices,
        IOptions<CognitiveMemoryOptions> options,
        ILogger<CognitiveMemoryService> logger)
    {
        ArgumentNullException.ThrowIfNull(analysisServices);
        ArgumentNullException.ThrowIfNull(innerMemoryService);
        _innerMemoryService = innerMemoryService;
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        _embeddingProvider = embeddingProvider;
        ArgumentNullException.ThrowIfNull(analysisServices.Analyzer);
        _analyzer = analysisServices.Analyzer;
        ArgumentNullException.ThrowIfNull(analysisServices.ContradictionDetector);
        _contradictionDetector = analysisServices.ContradictionDetector;
        ArgumentNullException.ThrowIfNull(analysisServices.Consolidator);
        _consolidator = analysisServices.Consolidator;
        ArgumentNullException.ThrowIfNull(analysisServices.Scorer);
        _scorer = analysisServices.Scorer;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    // --- IMemoryService delegation ---

    /// <inheritdoc />
    public ICrewMemorySystem GetMemorySystem(CrewId crewId) =>
        _innerMemoryService.GetMemorySystem(crewId);

    /// <inheritdoc />
    public void ReleaseMemorySystem(CrewId crewId) =>
        _innerMemoryService.ReleaseMemorySystem(crewId);

    /// <inheritdoc />
    public Task SaveMemoryAsync(CrewId crewId, MemoryItem item, CancellationToken cancellationToken = default) =>
        _innerMemoryService.SaveMemoryAsync(crewId, item, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId,
        string query,
        int maxResults = 10,
        DomainMemoryType? typeFilter = null,
        CancellationToken cancellationToken = default) =>
        _innerMemoryService.SearchMemoryAsync(crewId, query, maxResults, typeFilter, cancellationToken);

    /// <inheritdoc />
    public Task ClearMemoryAsync(CrewId crewId, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default) =>
        _innerMemoryService.ClearMemoryAsync(crewId, typeFilter, cancellationToken);

    // --- ICognitiveMemoryService ---

    /// <inheritdoc />
    public async Task<MemoryItem> RememberAsync(
        CrewId crewId,
        string content,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        LogRememberStart(content.Length);

        // Step 1: LLM analysis
        MemoryAnalysis? analysis = null;
        if (_options.EnableLlmAnalysis)
        {
            analysis = await _analyzer.AnalyzeAsync(content, context, cancellationToken).ConfigureAwait(false);
            LogAnalysisComplete(analysis.Importance, analysis.Category);
        }

        // Step 2: the vector — the contradiction candidates are found by it, and the item stored with it
        var embedding = await _embeddingProvider.GetEmbeddingAsync(content, cancellationToken).ConfigureAwait(false);
        var memory = CrewMemory(crewId);

        // Step 3: contradiction detection and resolution, in the crew's memory the candidates come from
        if (_options.EnableContradictionDetection)
        {
            var candidates = await CandidatesAsync(memory, embedding, cancellationToken).ConfigureAwait(false);
            var check = await _contradictionDetector.CheckAsync(
                content, candidates.Select(candidate => candidate.Item), cancellationToken).ConfigureAwait(false);
            if (check.HasContradiction)
            {
                var resolved = await ResolveConflictAsync(
                    memory, candidates, content, analysis, check, cancellationToken).ConfigureAwait(false);
                if (resolved is not null)
                    return resolved;
            }
        }

        // Step 4: the enriched item, stored once
        var item = MemoryItem.Create(
            content: content,
            embedding: embedding,
            importance: analysis?.Importance ?? MemoryDefaults.DefaultImportance,
            source: "cognitive",
            tags: analysis?.SuggestedTags.ToArray());

        if (analysis is not null)
        {
            item.AddCustomProperty("category", analysis.Category);
            item.AddCustomProperty("summary", analysis.Summary);
            if (context is not null)
            {
                item.AddCustomProperty("context", context);
            }
        }

        await memory.AddAsync(item).ConfigureAwait(false);

        LogRememberComplete(item.Id);
        return item;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredMemory>> RecallAsync(
        CrewId crewId,
        string query,
        RecallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        options ??= _options.DefaultRecallOptions;

        LogRecallStart(query.Length, options.TopK);

        var queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);

        // Over-fetch 3x for composite re-ranking, in the crew's memory only
        var semanticResults = await CrewMemory(crewId).SearchSimilarAsync(
            queryEmbedding, options.TopK * 3, 0.0f, cancellationToken).ConfigureAwait(false);

        var scored = _scorer.Score(semanticResults, options);

        LogRecallComplete(scored.Count);
        return scored;
    }

    /// <inheritdoc />
    public async Task<ConsolidationResult> ConsolidateAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        LogConsolidationStart(crewId);

        // The crew's memories, by similarity within its scope and with no score floor: every one of
        // them, up to the scan limit. An empty text search found them on no provider.
        var memory = CrewMemory(crewId);
        var probe = await _embeddingProvider.GetEmbeddingAsync(ConsolidationProbe, cancellationToken).ConfigureAwait(false);
        var memories = await memory.SearchSimilarAsync(probe, ConsolidationScanLimit, -1f, cancellationToken).ConfigureAwait(false);

        var result = await _consolidator.ConsolidateAsync(memories, memory, cancellationToken).ConfigureAwait(false);

        LogConsolidationComplete(result.MergedCount, result.PrunedCount);
        return result;
    }

    /// <inheritdoc />
    public async Task<MemoryAnalysis> AnalyzeAsync(
        CrewId crewId,
        string content,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        // Dry-run: analyze without storing
        return await _analyzer.AnalyzeAsync(content, context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ContradictionCheck> CheckContradictionsAsync(
        CrewId crewId,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var embedding = await _embeddingProvider.GetEmbeddingAsync(content, cancellationToken).ConfigureAwait(false);
        var candidates = await CandidatesAsync(CrewMemory(crewId), embedding, cancellationToken).ConfigureAwait(false);
        return await _contradictionDetector.CheckAsync(
            content, candidates.Select(candidate => candidate.Item), cancellationToken).ConfigureAwait(false);
    }

    // --- Private helpers ---

    /// <summary>The crew's long-term memory: its store, within its scope.</summary>
    private ILongTermMemory CrewMemory(CrewId crewId) => _innerMemoryService.GetMemorySystem(crewId).LongTerm;

    /// <summary>The crew's memories closest to <paramref name="embedding"/>: what new content may contradict.</summary>
    private Task<IReadOnlyList<ScoredMemoryItem>> CandidatesAsync(
        ILongTermMemory memory, float[] embedding, CancellationToken cancellationToken) =>
        memory.SearchSimilarAsync(embedding, _options.ContradictionCandidateCount, 0.0f, cancellationToken);

    /// <summary>The storage key of a found memory: the key its store returned, else its id.</summary>
    private static string KeyOf(ScoredMemoryItem found) => found.Key ?? found.Item.Id.ToString();

    private async Task<MemoryItem?> ResolveConflictAsync(
        ILongTermMemory memory,
        IReadOnlyList<ScoredMemoryItem> candidates,
        string content,
        MemoryAnalysis? analysis,
        ContradictionCheck check,
        CancellationToken cancellationToken)
    {
        LogConflictResolution(check.RecommendedAction);

        // The conflicting memories are among the candidates the detector was shown: resolved in the
        // memory they came from, by the key that memory returned.
        var conflicting = candidates
            .Where(candidate => check.ConflictingMemoryIds.Contains(candidate.Item.Id.ToString(), StringComparer.Ordinal))
            .ToList();

        switch (check.RecommendedAction)
        {
            case ConflictResolution.KeepExisting:
                // The first conflicting memory stands; none found: the new content is stored.
                return conflicting.FirstOrDefault()?.Item;

            case ConflictResolution.KeepNew:
                foreach (var old in conflicting)
                {
                    await memory.RemoveAsync(KeyOf(old), cancellationToken).ConfigureAwait(false);
                }
                return null;

            case ConflictResolution.Merge:
                foreach (var old in conflicting)
                {
                    await memory.RemoveAsync(KeyOf(old), cancellationToken).ConfigureAwait(false);
                }

                var mergedContent = string.Join(" | ", conflicting.Select(old => old.Item.Content).Prepend(content));
                var embedding = await _embeddingProvider.GetEmbeddingAsync(mergedContent, cancellationToken).ConfigureAwait(false);
                var merged = MemoryItem.Create(
                    content: mergedContent,
                    embedding: embedding,
                    importance: analysis?.Importance ?? MemoryDefaults.DefaultImportance,
                    source: "cognitive-merge",
                    tags: analysis?.SuggestedTags.ToArray());

                await memory.AddAsync(merged).ConfigureAwait(false);
                return merged;

            case ConflictResolution.KeepBoth:
            default:
                // Fall through to normal storage
                return null;
        }
    }

    // --- Structured logging ---

    [LoggerMessage(Level = LogLevel.Debug, Message = "Remember started for content of length {Length}")]
    private partial void LogRememberStart(int length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analysis complete: importance={Importance}, category={Category}")]
    private partial void LogAnalysisComplete(float importance, string category);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Remember complete, stored item {ItemId}")]
    private partial void LogRememberComplete(string itemId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recall started for query of length {Length}, topK={TopK}")]
    private partial void LogRecallStart(int length, int topK);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recall complete, returned {Count} items")]
    private partial void LogRecallComplete(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation started for crew {CrewId}")]
    private partial void LogConsolidationStart(CrewId crewId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation complete: merged={Merged}, pruned={Pruned}")]
    private partial void LogConsolidationComplete(int merged, int pruned);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolving conflict with action: {Action}")]
    private partial void LogConflictResolution(ConflictResolution action);
}
