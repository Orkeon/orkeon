using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Cognitive memory service that uses LLM analysis for intelligent memory storage (remember)
/// and retrieval (recall), with contradiction detection and automatic consolidation.
/// Extends <see cref="IMemoryService"/> with LLM-powered capabilities.
/// </summary>
public interface ICognitiveMemoryService : IMemoryService
{
    /// <summary>
    /// Analyzes content using LLM, checks for contradictions, generates embeddings,
    /// and stores an enriched memory item.
    /// </summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="content">The content to remember.</param>
    /// <param name="context">Optional context to help the LLM understand the content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The enriched memory item that was stored.</returns>
    System.Threading.Tasks.Task<MemoryItem> RememberAsync(
        CrewId crewId,
        string content,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves memories using semantic search with composite scoring
    /// (semantic similarity, recency, importance).
    /// </summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="query">The query to search for.</param>
    /// <param name="options">Optional recall options for tuning retrieval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ranked list of scored memories.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<ScoredMemory>> RecallAsync(
        CrewId crewId,
        string query,
        RecallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consolidates memories by merging redundant clusters and pruning low-value items.
    /// </summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of the consolidation operation.</returns>
    System.Threading.Tasks.Task<ConsolidationResult> ConsolidateAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a dry-run analysis of content without storing it.
    /// Useful for previewing how content would be categorized and scored.
    /// </summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="content">The content to analyze.</param>
    /// <param name="context">Optional context to help the LLM understand the content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM analysis result.</returns>
    System.Threading.Tasks.Task<MemoryAnalysis> AnalyzeAsync(
        CrewId crewId,
        string content,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether new content contradicts existing memories for a crew.
    /// </summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="content">The content to check for contradictions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The contradiction check result.</returns>
    System.Threading.Tasks.Task<ContradictionCheck> CheckContradictionsAsync(
        CrewId crewId,
        string content,
        CancellationToken cancellationToken = default);
}
