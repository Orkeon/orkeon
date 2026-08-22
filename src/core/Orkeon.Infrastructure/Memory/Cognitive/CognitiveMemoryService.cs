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
public sealed partial class CognitiveMemoryService : ICognitiveMemoryService
{
    private readonly IMemoryService _innerMemoryService;
    private readonly IMemoryProvider _memoryProvider;
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
    public CognitiveMemoryService(
        IMemoryService innerMemoryService,
        IMemoryProvider memoryProvider,
        IEmbeddingProvider embeddingProvider,
        CognitiveAnalysisServices analysisServices,
        IOptions<CognitiveMemoryOptions> options,
        ILogger<CognitiveMemoryService> logger)
    {
        ArgumentNullException.ThrowIfNull(analysisServices);
        ArgumentNullException.ThrowIfNull(innerMemoryService);
        _innerMemoryService = innerMemoryService;
        ArgumentNullException.ThrowIfNull(memoryProvider);
        _memoryProvider = memoryProvider;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        LogRememberStart(content.Length);

        // Step 1: LLM analysis
        MemoryAnalysis? analysis = null;
        if (_options.EnableLlmAnalysis)
        {
            analysis = await _analyzer.AnalyzeAsync(content, context, cancellationToken).ConfigureAwait(false);
            LogAnalysisComplete(analysis.Importance, analysis.Category);
        }

        // Step 2: Contradiction detection
        ContradictionCheck? contradictionCheck = null;
        if (_options.EnableContradictionDetection)
        {
            contradictionCheck = await CheckContradictionsInternalAsync(crewId, content, cancellationToken).ConfigureAwait(false);
        }

        // Step 3: Resolve conflicts
        if (contradictionCheck is { HasContradiction: true })
        {
            var resolved = await ResolveConflictAsync(
                crewId, content, analysis, contradictionCheck, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        // Step 4: Generate embedding
        var embedding = await _embeddingProvider.GetEmbeddingAsync(content, cancellationToken).ConfigureAwait(false);

        // Step 5: Create enriched MemoryItem
        var importance = analysis?.Importance ?? MemoryDefaults.DefaultImportance;
        var tags = analysis?.SuggestedTags.ToArray();
        var item = MemoryItem.Create(
            content: content,
            embedding: embedding,
            importance: importance,
            source: "cognitive",
            tags: tags);

        if (analysis is not null)
        {
            item.AddCustomProperty("category", analysis.Category);
            item.AddCustomProperty("summary", analysis.Summary);
            if (context is not null)
            {
                item.AddCustomProperty("context", context);
            }
        }

        // Step 6: Store
        await _memoryProvider.StoreWithEmbeddingAsync(item.Id, item, embedding, cancellationToken).ConfigureAwait(false);
        await _innerMemoryService.SaveMemoryAsync(crewId, item, cancellationToken).ConfigureAwait(false);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        options ??= _options.DefaultRecallOptions;

        LogRecallStart(query.Length, options.TopK);

        // Generate query embedding
        var queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);

        // Over-fetch 3x for composite re-ranking
        var overFetchCount = options.TopK * 3;
        var semanticResults = await _memoryProvider.SearchSimilarAsync(
            queryEmbedding,
            overFetchCount,
            0.0f,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Composite scoring
        var scored = _scorer.Score(semanticResults, options);

        LogRecallComplete(scored.Count);
        return scored;
    }

    /// <inheritdoc />
    public async Task<ConsolidationResult> ConsolidateAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default)
    {
        LogConsolidationStart(crewId);

        // Fetch all memories for this crew
        var allMemories = await _innerMemoryService.SearchMemoryAsync(
            crewId, "", int.MaxValue, cancellationToken: cancellationToken).ConfigureAwait(false);

        var result = await _consolidator.ConsolidateAsync(allMemories, cancellationToken).ConfigureAwait(false);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        return await CheckContradictionsInternalAsync(crewId, content, cancellationToken).ConfigureAwait(false);
    }

    // --- Private helpers ---

    private async Task<ContradictionCheck> CheckContradictionsInternalAsync(
        CrewId crewId,
        string content,
        CancellationToken cancellationToken)
    {
        // Search for similar existing memories
        var existingMemories = await _innerMemoryService.SearchMemoryAsync(
            crewId, content, _options.ContradictionCandidateCount, cancellationToken: cancellationToken).ConfigureAwait(false);

        return await _contradictionDetector.CheckAsync(content, existingMemories, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MemoryItem?> ResolveConflictAsync(
        CrewId crewId,
        string content,
        MemoryAnalysis? analysis,
        ContradictionCheck check,
        CancellationToken cancellationToken)
    {
        LogConflictResolution(check.RecommendedAction);

        switch (check.RecommendedAction)
        {
            case ConflictResolution.KeepExisting:
                // Return the first conflicting memory as the "existing" one
                if (check.ConflictingMemoryIds.Count > 0)
                {
                    var existing = await _memoryProvider.GetAsync(
                        check.ConflictingMemoryIds[0], cancellationToken).ConfigureAwait(false);
                    if (existing is not null)
                        return existing;
                }
                // If we can't find the existing, fall through to store new
                return null;

            case ConflictResolution.KeepNew:
                // Delete conflicting memories, then fall through to store new
                foreach (var id in check.ConflictingMemoryIds)
                {
                    await _memoryProvider.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                }
                return null;

            case ConflictResolution.Merge:
                // Fetch conflicting, merge content, then store merged
                var conflictContents = new List<string> { content };
                foreach (var id in check.ConflictingMemoryIds)
                {
                    var m = await _memoryProvider.GetAsync(id, cancellationToken).ConfigureAwait(false);
                    if (m is not null)
                    {
                        conflictContents.Add(m.Content);
                        await _memoryProvider.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                    }
                }

                var mergedContent = string.Join(" | ", conflictContents);
                var embedding = await _embeddingProvider.GetEmbeddingAsync(mergedContent, cancellationToken).ConfigureAwait(false);
                var importance = analysis?.Importance ?? MemoryDefaults.DefaultImportance;
                var tags = analysis?.SuggestedTags.ToArray();

                var merged = MemoryItem.Create(
                    content: mergedContent,
                    embedding: embedding,
                    importance: importance,
                    source: "cognitive-merge",
                    tags: tags);

                await _memoryProvider.StoreWithEmbeddingAsync(merged.Id, merged, embedding, cancellationToken).ConfigureAwait(false);
                await _innerMemoryService.SaveMemoryAsync(crewId, merged, cancellationToken).ConfigureAwait(false);
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
