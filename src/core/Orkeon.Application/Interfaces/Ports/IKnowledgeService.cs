using Orkeon.Application.Rag;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Service for managing knowledge and RAG operations.
/// </summary>
public interface IKnowledgeService
{
    /// <summary>
    /// Adds a knowledge source to the system.
    /// </summary>
    System.Threading.Tasks.Task<string> AddSourceAsync(
        IKnowledgeSource source,
        bool loadImmediately = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a knowledge source.
    /// </summary>
    System.Threading.Tasks.Task<bool> RemoveSourceAsync(
        string sourceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads or reloads knowledge from a source.
    /// </summary>
    System.Threading.Tasks.Task<int> LoadSourceAsync(
        string sourceName,
        KnowledgeLoadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for relevant knowledge based on a query.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<KnowledgeItem>> SearchAsync(
        string query,
        int topK = 5,
        double minSimilarity = SearchDefaults.DefaultSimilarityThreshold,
        string[]? sources = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets knowledge context for a specific task or query.
    /// </summary>
    System.Threading.Tasks.Task<KnowledgeContext> GetContextAsync(
        string query,
        string? agentId = null,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds knowledge directly without a source.
    /// </summary>
    System.Threading.Tasks.Task<string> AddKnowledgeAsync(
        string content,
        Dictionary<string, object>? metadata = null,
        string? source = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates existing knowledge.
    /// </summary>
    System.Threading.Tasks.Task<bool> UpdateKnowledgeAsync(
        string id,
        string content,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes knowledge by ID.
    /// </summary>
    System.Threading.Tasks.Task<bool> DeleteKnowledgeAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about the knowledge base.
    /// </summary>
    System.Threading.Tasks.Task<KnowledgeStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes all knowledge sources that have updates.
    /// </summary>
    System.Threading.Tasks.Task<KnowledgeRefreshResult> RefreshSourcesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports knowledge to a file.
    /// </summary>
    System.Threading.Tasks.Task ExportAsync(
        string filePath,
        KnowledgeExportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports knowledge from a file.
    /// </summary>
    System.Threading.Tasks.Task<int> ImportAsync(
        string filePath,
        KnowledgeImportOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics about the knowledge base.
/// </summary>
public class KnowledgeStatistics
{
    /// <summary>Gets or sets the total items.</summary>
    public long TotalItems { get; init; }
    /// <summary>Gets or sets the source count.</summary>
    public int SourceCount { get; init; }
    /// <summary>Items Per Source.</summary>
    public Dictionary<string, long> ItemsPerSource { get; init; } = [];
    /// <summary>Gets or sets the last update.</summary>
    public DateTime LastUpdate { get; init; }
    /// <summary>Gets or sets the total size bytes.</summary>
    public long TotalSizeBytes { get; init; }
    /// <summary>Gets or sets the average embedding dimension.</summary>
    public double AverageEmbeddingDimension { get; init; }
}

/// <summary>
/// Result of refreshing knowledge sources.
/// </summary>
public class KnowledgeRefreshResult
{
    /// <summary>Gets or sets the sources checked.</summary>
    public int SourcesChecked { get; init; }
    /// <summary>Gets or sets the sources updated.</summary>
    public int SourcesUpdated { get; init; }
    /// <summary>Gets or sets the items added.</summary>
    public int ItemsAdded { get; init; }
    /// <summary>Gets or sets the items updated.</summary>
    public int ItemsUpdated { get; init; }
    /// <summary>Gets or sets the items removed.</summary>
    public int ItemsRemoved { get; init; }
    /// <summary>Gets or sets the errors.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Options for exporting knowledge.
/// </summary>
public class KnowledgeExportOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether include embeddings.
    /// </summary>
    public bool IncludeEmbeddings { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether include metadata.
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;
    /// <summary>Gets or sets the sources.</summary>
    public IReadOnlyList<string>? Sources { get; set; }
    /// <summary>Gets or sets the format.</summary>
    public string Format { get; set; } = "json"; // json, csv, parquet
}

/// <summary>
/// Options for importing knowledge.
/// </summary>
public class KnowledgeImportOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether overwrite existing.
    /// </summary>
    public bool OverwriteExisting { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether regenerate embeddings.
    /// </summary>
    public bool RegenerateEmbeddings { get; set; }
    /// <summary>Gets or sets the default source.</summary>
    public string? DefaultSource { get; set; }
    /// <summary>Default Metadata.</summary>
    public Dictionary<string, object>? DefaultMetadata { get; init; }
}
