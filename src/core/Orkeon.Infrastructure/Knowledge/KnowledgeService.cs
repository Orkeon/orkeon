using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Text.Json;
using Orkeon.Application.Constants.Rag;
using Orkeon.Application.Rag;
using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.Knowledge;

/// <summary>
/// In-memory implementation of IKnowledgeService.
/// Stores knowledge items using a ConcurrentDictionary, embeds them at ingestion via
/// <see cref="IEmbeddingProvider"/>, and serves searches through the memory provider's
/// cosine vector search (<see cref="IMemoryProvider.SearchSimilarAsync"/>).
/// Supports source management, export/import.
/// </summary>
public partial class KnowledgeService : IKnowledgeService
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true, MaxDepth = SerializationDefaults.JsonMaxDepth };

    /// <summary>Custom property key linking a stored memory item back to its knowledge item.</summary>
    private const string KnowledgeIdProperty = "knowledge_id";

    /// <summary>
    /// Oversampling factor applied to the vector-search candidate window when a source
    /// filter is requested, since source filtering happens after the provider search.
    /// </summary>
    private const int SourceFilterOversampling = 4;

    private readonly ConcurrentDictionary<string, KnowledgeItem> _items = new();
    private readonly ConcurrentDictionary<string, IKnowledgeSource> _sources = new();
    private readonly ConcurrentDictionary<string, List<string>> _sourceItemIds = new();
    private readonly ITextChunker _chunker;
    private readonly IFileSystemService _fs;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IMemoryProvider _memory;
    private readonly ILogger _logger;
    private DateTime _lastUpdate = DateTime.UtcNow;

    /// <summary>Initializes a new instance of <see cref="KnowledgeService"/>.</summary>
    /// <param name="chunker">The text chunker for splitting knowledge content.</param>
    /// <param name="fs">Virtual file system for export/import operations. Required.</param>
    /// <param name="embeddings">Embedding provider used to embed items at ingestion and queries at search time. Required.</param>
    /// <param name="memory">Memory provider hosting the vector index (cosine similarity search). Required.</param>
    /// <param name="logger">Optional logger.</param>
    public KnowledgeService(
        ITextChunker chunker,
        IFileSystemService fs,
        IEmbeddingProvider embeddings,
        IMemoryProvider memory,
        ILogger<KnowledgeService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(memory);
        _chunker = chunker;
        _fs = fs;
        _embeddings = embeddings;
        _memory = memory;
        _logger = logger ?? NullLogger<KnowledgeService>.Instance;
    }

    /// <inheritdoc />
    public Task<string> AddSourceAsync(
        IKnowledgeSource source,
        bool loadImmediately = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return AddSourceCoreAsync(source, loadImmediately, cancellationToken);

        async Task<string> AddSourceCoreAsync(
            IKnowledgeSource source, bool loadImmediately, CancellationToken cancellationToken)
        {
            _sources[source.Name] = source;
            _sourceItemIds.TryAdd(source.Name, []);

            LogAddedKnowledgeSource(source.Name, source.Type);

            if (loadImmediately)
            {
                await LoadSourceAsync(source.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return source.Name;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveSourceAsync(
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return false;

        var removed = _sources.TryRemove(sourceName, out _);

        if (removed && _sourceItemIds.TryRemove(sourceName, out var itemIds))
        {
            await RemoveFromIndexAsync(itemIds, cancellationToken).ConfigureAwait(false);
        }

        _lastUpdate = DateTime.UtcNow;
        LogRemovedKnowledgeSource(sourceName, removed);
        return removed;
    }

    /// <inheritdoc />
    public async Task<int> LoadSourceAsync(
        string sourceName,
        KnowledgeLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sources.TryGetValue(sourceName, out var source))
            throw new InvalidOperationException($"Knowledge source not found: {sourceName}");

        // Remove existing items for this source if reloading
        if (_sourceItemIds.TryGetValue(sourceName, out var existingIds))
        {
            await RemoveFromIndexAsync(existingIds, cancellationToken).ConfigureAwait(false);
            existingIds.Clear();
        }

        var content = await source.GetContentAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<TextChunk> chunks = _chunker.Chunk(content.Content);

        if (options?.MaxItems is int maxItems)
        {
            chunks = chunks.Take(maxItems);
        }

        var chunkList = chunks.ToList();
        var itemIds = _sourceItemIds.GetOrAdd(sourceName, _ => []);

        // Embed all chunks at ingestion (single batch call)
        var embeddings = chunkList.Count > 0
            ? await _embeddings.GetEmbeddingsAsync(chunkList.Select(c => c.Content).ToList(), cancellationToken).ConfigureAwait(false)
            : [];

        for (int i = 0; i < chunkList.Count; i++)
        {
            var chunk = chunkList[i];
            var item = KnowledgeItem.Create(
                content: chunk.Content,
                source: sourceName,
                metadata: new Dictionary<string, object>
                {
                    ["chunk_start"] = chunk.StartIndex,
                    ["chunk_end"] = chunk.EndIndex,
                    ["source_type"] = source.Type
                });

            await IndexAsync(item, embeddings[i], cancellationToken).ConfigureAwait(false);
            itemIds.Add(item.Id);
        }

        _lastUpdate = DateTime.UtcNow;
        LogLoadedItemsFromSource(chunkList.Count, sourceName);
        return chunkList.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeItem>> SearchAsync(
        string query,
        int topK = 5,
        double minSimilarity = RagDefaults.DefaultMinRelevanceScore,
        string[]? sources = null,
        IDictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || topK <= 0)
            return Array.Empty<KnowledgeItem>();

        var queryEmbedding = await _embeddings.GetEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);

        var sourceSet = sources != null ? new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase) : null;
        var hasFilters = filters is { Count: > 0 };
        var candidateTopK = sourceSet is null && !hasFilters ? topK : topK * SourceFilterOversampling;

        var scored = await _memory.SearchSimilarAsync(
            queryEmbedding,
            topK: candidateTopK,
            minScore: (float)minSimilarity,
            filter: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var results = new List<KnowledgeItem>(Math.Min(topK, scored.Count));

        foreach (var match in scored)
        {
            if (!TryResolveKnowledgeItem(match.Item, out var item))
                continue;

            if (sourceSet != null && !sourceSet.Contains(item.Source))
                continue;

            if (!MatchesFilters(item, filters))
                continue;

            results.Add(item with { SimilarityScore = match.Score });

            if (results.Count >= topK)
                break;
        }

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<KnowledgeContext> GetContextAsync(
        string query,
        string? agentId = null,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var context = new KnowledgeContext();

        var items = await SearchAsync(query, topK: 10, cancellationToken: cancellationToken).ConfigureAwait(false);
        context.AddRelevantKnowledge(items);

        context.SetMetadata("query", query);
        context.SetMetadata("timestamp", DateTime.UtcNow);
        context.SetMetadata("item_count", items.Count);

        if (agentId != null)
            context.SetMetadata("agent_id", agentId);

        if (filters != null)
        {
            foreach (var kvp in filters)
            {
                context.SetMetadata($"filter_{kvp.Key}", kvp.Value);
            }
        }

        return context;
    }

    /// <inheritdoc />
    public async Task<string> AddKnowledgeAsync(
        string content,
        Dictionary<string, object>? metadata = null,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var embedding = await _embeddings.GetEmbeddingAsync(content, cancellationToken).ConfigureAwait(false);

        var item = KnowledgeItem.Create(
            content: content,
            source: source ?? "direct",
            metadata: metadata);

        await IndexAsync(item, embedding, cancellationToken).ConfigureAwait(false);

        if (source != null)
        {
            var itemIds = _sourceItemIds.GetOrAdd(source, _ => []);
            itemIds.Add(item.Id);
        }

        _lastUpdate = DateTime.UtcNow;
        return item.Id;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateKnowledgeAsync(
        string id,
        string content,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        if (!_items.TryGetValue(id, out var existing))
            return false;

        var embedding = await _embeddings.GetEmbeddingAsync(content, cancellationToken).ConfigureAwait(false);

        var updated = existing with
        {
            Content = content,
            Metadata = metadata ?? existing.Metadata
        };

        await IndexAsync(updated, embedding, cancellationToken).ConfigureAwait(false);
        _lastUpdate = DateTime.UtcNow;
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteKnowledgeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var removed = _items.TryRemove(id, out var item);

        if (removed && item != null)
        {
            await _memory.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

            // Remove from source tracking
            foreach (var kvp in _sourceItemIds)
            {
                kvp.Value.Remove(id);
            }
        }

        _lastUpdate = DateTime.UtcNow;
        return removed;
    }

    /// <inheritdoc />
    public Task<KnowledgeStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var itemsPerSource = _sourceItemIds.Select(kvp => new KeyValuePair<string, long>(kvp.Key, kvp.Value.Count))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Count items not tracked by any source
        var trackedIds = new HashSet<string>(_sourceItemIds.Values.SelectMany(ids => ids));
        var untrackedCount = _items.Keys.Count(id => !trackedIds.Contains(id));
        if (untrackedCount > 0)
        {
            itemsPerSource["direct"] = untrackedCount;
        }

        long totalSize = _items.Values.Sum(item => (long)(item.Content?.Length ?? 0));

        var embeddedDimensions = _items.Values
            .Where(item => item.Embedding is { Count: > 0 })
            .Select(item => (double)item.Embedding!.Count)
            .ToList();

        var stats = new KnowledgeStatistics
        {
            TotalItems = _items.Count,
            SourceCount = _sources.Count,
            ItemsPerSource = itemsPerSource,
            LastUpdate = _lastUpdate,
            TotalSizeBytes = totalSize,
            AverageEmbeddingDimension = embeddedDimensions.Count > 0 ? embeddedDimensions.Average() : 0
        };

        return Task.FromResult(stats);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-source fault barrier: a failure refreshing one knowledge source is recorded in the errors list and the remaining sources are still refreshed.")]
    public async Task<KnowledgeRefreshResult> RefreshSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new KnowledgeRefreshResult
        {
            SourcesChecked = _sources.Count
        };

        var errors = new List<string>();
        int sourcesUpdated = 0;
        int totalAdded = 0;

        foreach (var key in _sources.Select(kvp => kvp.Key))
        {
            try
            {
                var loaded = await LoadSourceAsync(key, cancellationToken: cancellationToken).ConfigureAwait(false);
                sourcesUpdated++;
                totalAdded += loaded;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to refresh source '{key}': {ex.Message}");
            }
        }

        return new KnowledgeRefreshResult
        {
            SourcesChecked = result.SourcesChecked,
            SourcesUpdated = sourcesUpdated,
            ItemsAdded = totalAdded,
            ItemsUpdated = 0,
            ItemsRemoved = 0,
            Errors = errors
        };
    }

    /// <inheritdoc />
    public async Task ExportAsync(
        string filePath,
        KnowledgeExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        options ??= new KnowledgeExportOptions();

        var itemsToExport = _items.Values.AsEnumerable();

        if (options.Sources != null && options.Sources.Count > 0)
        {
            var sourceSet = new HashSet<string>(options.Sources, StringComparer.OrdinalIgnoreCase);
            itemsToExport = itemsToExport.Where(item => sourceSet.Contains(item.Source));
        }

        var exportData = itemsToExport.Select(item => new KnowledgeExportEntry
        {
            Id = item.Id,
            Content = item.Content,
            Source = item.Source,
            Metadata = options.IncludeMetadata ? item.Metadata : null,
            CreatedAt = item.CreatedAt
        }).ToList();

        var json = JsonSerializer.Serialize(exportData, s_indentedOptions);

        await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> ImportAsync(
        string filePath,
        KnowledgeImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var json = await _fs.TryReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (json is null)
            throw new FileNotFoundException($"Import file not found: {filePath}", filePath);

        options ??= new KnowledgeImportOptions();

        var entries = JsonSerializer.Deserialize<List<KnowledgeExportEntry>>(json)
            ?? throw new InvalidOperationException("Failed to deserialize import file.");

        int count = 0;
        var pendingIndex = new List<KnowledgeItem>();

        foreach (var entry in entries)
        {
            if (!options.OverwriteExisting && _items.ContainsKey(entry.Id))
                continue;

            var metadata = entry.Metadata ?? options.DefaultMetadata;
            var source = entry.Source ?? options.DefaultSource ?? "imported";

            var item = new KnowledgeItem(
                Id: entry.Id,
                Content: entry.Content,
                Source: source,
                Metadata: metadata,
                CreatedAt: entry.CreatedAt ?? DateTime.UtcNow);

            _items[item.Id] = item;

            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                pendingIndex.Add(item);
            }

            var itemIds = _sourceItemIds.GetOrAdd(source, _ => []);
            if (!itemIds.Contains(item.Id))
                itemIds.Add(item.Id);

            count++;
        }

        // Embed all imported items at ingestion (single batch call)
        if (pendingIndex.Count > 0)
        {
            var embeddings = await _embeddings.GetEmbeddingsAsync(
                pendingIndex.Select(item => item.Content).ToList(),
                cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < pendingIndex.Count; i++)
            {
                await IndexAsync(pendingIndex[i], embeddings[i], cancellationToken).ConfigureAwait(false);
            }
        }

        _lastUpdate = DateTime.UtcNow;
        return count;
    }

    /// <summary>
    /// Metadata post-filter applied to vector-search candidates (RAG-01/C5): <c>source</c> matches
    /// the item source, any other key is matched against the item's metadata by string equality.
    /// </summary>
    private static bool MatchesFilters(KnowledgeItem item, IDictionary<string, object>? filters)
    {
        if (filters == null || filters.Count == 0)
            return true;

        foreach (var (key, value) in filters)
        {
            var expected = value?.ToString();

            if (string.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(item.Source, expected, StringComparison.OrdinalIgnoreCase))
                    return false;
                continue;
            }

            if (item.Metadata == null ||
                !item.Metadata.TryGetValue(key, out var actual) ||
                !string.Equals(actual?.ToString(), expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Stores an embedded knowledge item in the local catalog and in the memory provider's
    /// vector index, keyed by the knowledge item id and carrying a back-reference in
    /// custom properties for search-time resolution.
    /// </summary>
    private async Task<KnowledgeItem> IndexAsync(
        KnowledgeItem item,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        var embedded = item with { Embedding = embedding };
        _items[embedded.Id] = embedded;

        var memoryItem = MemoryItem.Create(
            content: embedded.Content,
            source: embedded.Source,
            customProperties: new Dictionary<string, string> { [KnowledgeIdProperty] = embedded.Id });

        await _memory.StoreWithEmbeddingAsync(embedded.Id, memoryItem, embedding, cancellationToken).ConfigureAwait(false);
        return embedded;
    }

    /// <summary>Removes items from both the local catalog and the vector index.</summary>
    private async Task RemoveFromIndexAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken)
    {
        foreach (var id in ids.ToList())
        {
            _items.TryRemove(id, out _);
            await _memory.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves a memory item returned by the vector search back to its knowledge item.
    /// </summary>
    private bool TryResolveKnowledgeItem(MemoryItem memoryItem, out KnowledgeItem item)
    {
        item = null!;
        var props = memoryItem.Metadata.CustomProperties;
        return props != null
            && props.TryGetValue(KnowledgeIdProperty, out var id)
            && _items.TryGetValue(id, out item!);
    }

    /// <summary>
    /// Data structure for export/import serialization.
    /// </summary>
    private sealed class KnowledgeExportEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public Dictionary<string, object>? Metadata { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Added knowledge source: {SourceName} (type: {SourceType})")]
    private partial void LogAddedKnowledgeSource(string sourceName, string sourceType);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Removed knowledge source: {SourceName} (found: {Found})")]
    private partial void LogRemovedKnowledgeSource(string sourceName, bool found);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Loaded {Count} items from source: {SourceName}")]
    private partial void LogLoadedItemsFromSource(int count, string sourceName);
}
