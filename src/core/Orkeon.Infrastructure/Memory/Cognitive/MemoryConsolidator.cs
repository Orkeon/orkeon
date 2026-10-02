using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Infrastructure.Memory.Cognitive;

/// <summary>
/// Consolidates memories by clustering semantically similar items,
/// merging redundant clusters via LLM, and pruning low-value entries — in the memory they were
/// found in, the crew's (GAP-30): a merged memory is embedded and stored there, the memories it
/// replaces and the pruned ones are removed from it.
/// </summary>
public sealed partial class MemoryConsolidator
{
    private readonly ILlmProvider _llmProvider;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly CognitiveMemoryOptions _options;
    private readonly ILogger<MemoryConsolidator> _logger;

    /// <summary>Minimum number of memories required to trigger consolidation.</summary>
    private const int MinMemoriesForConsolidation = 5;

    /// <summary>Cosine similarity threshold for clustering.</summary>
    private const float ClusterThreshold = Orkeon.Infrastructure.Constants.Memory.SearchDefaults.DefaultSimilarityThreshold;

    /// <summary>Initializes a new instance of <see cref="MemoryConsolidator"/>.</summary>
    /// <param name="llmProvider">Merges the memories of a cluster into one.</param>
    /// <param name="embeddingProvider">Embeds a merged memory, which is found by its vector like any other.</param>
    /// <param name="options">The cognitive memory options.</param>
    /// <param name="logger">The logger.</param>
    public MemoryConsolidator(
        ILlmProvider llmProvider,
        IEmbeddingProvider embeddingProvider,
        IOptions<CognitiveMemoryOptions> options,
        ILogger<MemoryConsolidator> logger)
    {
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        _embeddingProvider = embeddingProvider;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Consolidates the given memories, found in <paramref name="memory"/>, by merging redundant
    /// clusters and pruning low-value items there.
    /// </summary>
    /// <param name="memories">The memories to consolidate, with the key their memory returned.</param>
    /// <param name="memory">The memory they were found in: where merges are stored and removals made.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ScoredMemoryItem> memories,
        ILongTermMemory memory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memories);
        ArgumentNullException.ThrowIfNull(memory);
        var memoryList = memories.ToList();

        if (memoryList.Count < MinMemoriesForConsolidation)
        {
            LogSkipConsolidation(memoryList.Count, MinMemoriesForConsolidation);
            return new ConsolidationResult
            {
                MergedCount = 0,
                PrunedCount = 0,
                UnchangedCount = memoryList.Count,
                CreatedMemoryIds = Array.Empty<string>()
            };
        }

        // Phase 1: Cluster by semantic similarity
        var clusters = ClusterMemories(memoryList);
        LogClustersFound(clusters.Count);

        // Phase 2: Merge multi-item clusters via LLM
        var (mergedCount, createdIds) = await MergeClustersAsync(clusters, memory, cancellationToken).ConfigureAwait(false);

        // Phase 3: Prune low-importance, old, never-accessed memories — none of those just merged
        var merged = clusters.Where(c => c.Count > 1).SelectMany(c => c).Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        var prunedCount = await PruneLowValueMemoriesAsync(
            memoryList.Where(m => !merged.Contains(KeyOf(m))).ToList(), memory, cancellationToken).ConfigureAwait(false);

        var unchangedCount = memoryList.Count - mergedCount - prunedCount;

        LogConsolidationComplete(mergedCount, prunedCount, unchangedCount);

        return new ConsolidationResult
        {
            MergedCount = mergedCount,
            PrunedCount = prunedCount,
            UnchangedCount = Math.Max(0, unchangedCount),
            CreatedMemoryIds = createdIds
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-cluster fault barrier: a failure merging or storing one cluster is logged and skipped so the remaining clusters are still consolidated.")]
    private async Task<(int MergedCount, List<string> CreatedIds)> MergeClustersAsync(
        List<List<ScoredMemoryItem>> clusters, ILongTermMemory memory, CancellationToken cancellationToken)
    {
        var mergedCount = 0;
        var createdIds = new List<string>();

        foreach (var cluster in clusters.Where(c => c.Count > 1))
        {
            try
            {
                var merged = await MergeClusterAsync(cluster, cancellationToken).ConfigureAwait(false);
                if (merged is not null)
                {
                    await memory.AddAsync(merged).ConfigureAwait(false);
                    createdIds.Add(merged.Id);

                    foreach (var old in cluster)
                    {
                        await memory.RemoveAsync(KeyOf(old), cancellationToken).ConfigureAwait(false);
                    }

                    mergedCount += cluster.Count;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMergeError(ex, cluster.Count);
            }
        }

        return (mergedCount, createdIds);
    }

    private async Task<int> PruneLowValueMemoriesAsync(
        List<ScoredMemoryItem> memoryList, ILongTermMemory memory, CancellationToken cancellationToken)
    {
        var prunedCount = 0;
        var cutoff = DateTime.UtcNow.AddDays(-_options.PruningMinAgeDays);

        foreach (var found in memoryList)
        {
            var item = found.Item;
            if (item.Importance < _options.PruningThreshold
                && item.AccessCount == 0
                && item.Timestamp < cutoff)
            {
                await memory.RemoveAsync(KeyOf(found), cancellationToken).ConfigureAwait(false);
                prunedCount++;
            }
        }

        return prunedCount;
    }

    /// <summary>The storage key of a found memory: the key its memory returned, else its id.</summary>
    private static string KeyOf(ScoredMemoryItem found) => found.Key ?? found.Item.Id.ToString();

    /// <summary>
    /// Clusters memories by cosine similarity of their embeddings.
    /// Memories without embeddings are placed in their own single-item clusters.
    /// </summary>
    internal static List<List<ScoredMemoryItem>> ClusterMemories(List<ScoredMemoryItem> items)
    {
        var assigned = new bool[items.Count];
        var clusters = new List<List<ScoredMemoryItem>>();

        for (var i = 0; i < items.Count; i++)
        {
            if (assigned[i]) continue;

            assigned[i] = true;
            var cluster = new List<ScoredMemoryItem> { items[i] };

            if (items[i].Item.Embedding is not null)
                AssignSimilarItems(items, assigned, cluster, i);

            clusters.Add(cluster);
        }

        return clusters;
    }

    private static void AssignSimilarItems(
        List<ScoredMemoryItem> items, bool[] assigned, List<ScoredMemoryItem> cluster, int anchorIndex)
    {
        for (var j = anchorIndex + 1; j < items.Count; j++)
        {
            if (assigned[j] || items[j].Item.Embedding is null) continue;

            var similarity = CosineSimilarity(items[anchorIndex].Item.Embedding!.ToArray(), items[j].Item.Embedding!.ToArray());
            if (similarity >= ClusterThreshold)
            {
                cluster.Add(items[j]);
                assigned[j] = true;
            }
        }
    }

    private async Task<MemoryItem?> MergeClusterAsync(
        List<ScoredMemoryItem> cluster,
        CancellationToken cancellationToken)
    {
        var contents = string.Join("\n---\n", cluster.Select(m => m.Item.Content));
        var prompt = $"""
            The following {cluster.Count} memory entries are semantically similar and should be merged into a single, concise memory.
            Preserve all unique information. Return ONLY the merged text, nothing else.

            Entries:
            {contents}
            """;

        // No AnalysisModel: the provider's own model, never OpenAI's on another vendor (GAP-18).
        var config = (string.IsNullOrWhiteSpace(_options.AnalysisModel)
                ? LlmConfig.OnProfile()
                : LlmConfig.Create(_options.AnalysisModel)) with
        {
            Temperature = _options.AnalysisTemperature,
            MaxTokens = 500
        };

        var messages = new[]
        {
            LlmMessage.System("You merge redundant memory entries into a single concise entry."),
            LlmMessage.User(prompt)
        };

        using var usageScope = LlmUsageScope.Begin(LlmUsageOperations.Memory);
        var response = await _llmProvider.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        var mergedContent = response.Content.Trim();

        if (string.IsNullOrWhiteSpace(mergedContent))
            return null;

        // Use the highest importance from the cluster
        var maxImportance = cluster.Max(m => m.Item.Importance);

        // Combine tags from all items
        var allTags = cluster.SelectMany(m => m.Item.Tags).Distinct().ToArray();

        // Embedded like any memory: found by its vector, and accepted by the stores that require one.
        var embedding = await _embeddingProvider.GetEmbeddingAsync(mergedContent, cancellationToken).ConfigureAwait(false);

        return MemoryItem.Create(
            content: mergedContent,
            embedding: embedding,
            importance: maxImportance,
            source: "consolidation",
            tags: allTags);
    }

    internal static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

        float dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var magnitude = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return magnitude == 0 ? 0f : dot / magnitude;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping consolidation: {Count} memories below minimum of {Minimum}")]
    private partial void LogSkipConsolidation(int count, int minimum);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} clusters for consolidation")]
    private partial void LogClustersFound(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to merge cluster of {Count} items")]
    private partial void LogMergeError(Exception ex, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation complete: merged={Merged}, pruned={Pruned}, unchanged={Unchanged}")]
    private partial void LogConsolidationComplete(int merged, int pruned, int unchanged);
}
