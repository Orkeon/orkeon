using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.Memory.Cognitive;

/// <summary>
/// Consolidates memories by clustering semantically similar items,
/// merging redundant clusters via LLM, and pruning low-value entries.
/// </summary>
public sealed partial class MemoryConsolidator
{
    private readonly ILlmProvider _llmProvider;
    private readonly IMemoryProvider _memoryProvider;
    private readonly CognitiveMemoryOptions _options;
    private readonly ILogger<MemoryConsolidator> _logger;

    /// <summary>Minimum number of memories required to trigger consolidation.</summary>
    private const int MinMemoriesForConsolidation = 5;

    /// <summary>Cosine similarity threshold for clustering.</summary>
    private const float ClusterThreshold = Orkeon.Infrastructure.Constants.Memory.SearchDefaults.DefaultSimilarityThreshold;

    /// <summary>Initializes a new instance of <see cref="MemoryConsolidator"/>.</summary>
    public MemoryConsolidator(
        ILlmProvider llmProvider,
        IMemoryProvider memoryProvider,
        IOptions<CognitiveMemoryOptions> options,
        ILogger<MemoryConsolidator> logger)
    {
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
        ArgumentNullException.ThrowIfNull(memoryProvider);
        _memoryProvider = memoryProvider;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Consolidates the given memories by merging redundant clusters and pruning low-value items.
    /// </summary>
    public async Task<ConsolidationResult> ConsolidateAsync(
        IEnumerable<MemoryItem> memories,
        CancellationToken cancellationToken)
    {
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
        var (mergedCount, createdIds) = await MergeClustersAsync(clusters, cancellationToken).ConfigureAwait(false);

        // Phase 3: Prune low-importance, old, never-accessed memories
        var prunedCount = await PruneLowValueMemoriesAsync(memoryList, cancellationToken).ConfigureAwait(false);

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
        List<List<MemoryItem>> clusters, CancellationToken cancellationToken)
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
                    await _memoryProvider.StoreAsync(merged.Id, merged, cancellationToken).ConfigureAwait(false);
                    createdIds.Add(merged.Id);

                    foreach (var old in cluster)
                    {
                        await _memoryProvider.DeleteAsync(old.Id, cancellationToken).ConfigureAwait(false);
                    }

                    mergedCount += cluster.Count;
                }
            }
            catch (Exception ex)
            {
                LogMergeError(ex, cluster.Count);
            }
        }

        return (mergedCount, createdIds);
    }

    private async Task<int> PruneLowValueMemoriesAsync(
        List<MemoryItem> memoryList, CancellationToken cancellationToken)
    {
        var prunedCount = 0;
        var cutoff = DateTime.UtcNow.AddDays(-_options.PruningMinAgeDays);

        foreach (var item in memoryList)
        {
            if (item.Importance < _options.PruningThreshold
                && item.AccessCount == 0
                && item.Timestamp < cutoff)
            {
                await _memoryProvider.DeleteAsync(item.Id, cancellationToken).ConfigureAwait(false);
                prunedCount++;
            }
        }

        return prunedCount;
    }

    /// <summary>
    /// Clusters memories by cosine similarity of their embeddings.
    /// Memories without embeddings are placed in their own single-item clusters.
    /// </summary>
    internal static List<List<MemoryItem>> ClusterMemories(List<MemoryItem> items)
    {
        var assigned = new bool[items.Count];
        var clusters = new List<List<MemoryItem>>();

        for (var i = 0; i < items.Count; i++)
        {
            if (assigned[i]) continue;

            assigned[i] = true;
            var cluster = new List<MemoryItem> { items[i] };

            if (items[i].Embedding is not null)
                AssignSimilarItems(items, assigned, cluster, i);

            clusters.Add(cluster);
        }

        return clusters;
    }

    private static void AssignSimilarItems(
        List<MemoryItem> items, bool[] assigned, List<MemoryItem> cluster, int anchorIndex)
    {
        for (var j = anchorIndex + 1; j < items.Count; j++)
        {
            if (assigned[j] || items[j].Embedding is null) continue;

            var similarity = CosineSimilarity(items[anchorIndex].Embedding!.ToArray(), items[j].Embedding!.ToArray());
            if (similarity >= ClusterThreshold)
            {
                cluster.Add(items[j]);
                assigned[j] = true;
            }
        }
    }

    private async Task<MemoryItem?> MergeClusterAsync(
        List<MemoryItem> cluster,
        CancellationToken cancellationToken)
    {
        var contents = string.Join("\n---\n", cluster.Select(m => m.Content));
        var prompt = $"""
            The following {cluster.Count} memory entries are semantically similar and should be merged into a single, concise memory.
            Preserve all unique information. Return ONLY the merged text, nothing else.

            Entries:
            {contents}
            """;

        var config = LlmConfig.Create(_options.AnalysisModel ?? LlmDefaults.DefaultModelName) with
        {
            Temperature = _options.AnalysisTemperature,
            MaxTokens = 500
        };

        var messages = new[]
        {
            LlmMessage.System("You merge redundant memory entries into a single concise entry."),
            LlmMessage.User(prompt)
        };

        var response = await _llmProvider.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        var mergedContent = response.Content.Trim();

        if (string.IsNullOrWhiteSpace(mergedContent))
            return null;

        // Use the highest importance from the cluster
        var maxImportance = cluster.Max(m => m.Importance);

        // Combine tags from all items
        var allTags = cluster.SelectMany(m => m.Tags).Distinct().ToArray();

        return MemoryItem.Create(
            content: mergedContent,
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
