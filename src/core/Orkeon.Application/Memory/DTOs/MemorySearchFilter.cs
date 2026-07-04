using System.Text.Json.Serialization;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Memory.DTOs;

/// <summary>
/// Strongly typed filter for memory search operations, replacing Dictionary&lt;string, object&gt; parameters.
/// </summary>
public sealed record MemorySearchFilter
{
    /// <summary>
    /// Filter results by agent identifier.
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    /// <summary>
    /// Filter results by task identifier.
    /// </summary>
    [JsonPropertyName("task_id")]
    public string? TaskId { get; init; }

    /// <summary>
    /// Filter results by crew identifier.
    /// </summary>
    [JsonPropertyName("crew_id")]
    public string? CrewId { get; init; }

    /// <summary>
    /// Filter results by memory type.
    /// </summary>
    [JsonPropertyName("memory_type")]
    public MemoryType? MemoryType { get; init; }

    /// <summary>
    /// Filter results to items created after this date.
    /// </summary>
    [JsonPropertyName("after")]
    public DateTime? After { get; init; }

    /// <summary>
    /// Filter results to items created before this date.
    /// </summary>
    [JsonPropertyName("before")]
    public DateTime? Before { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    [JsonPropertyName("top_k")]
    public int? TopK { get; init; }

    /// <summary>
    /// Minimum similarity score threshold for results.
    /// </summary>
    [JsonPropertyName("min_score")]
    public double? MinScore { get; init; }

    /// <summary>
    /// Additional tag-based filters.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Extensibility bag for provider-specific filter parameters.
    /// </summary>
    [JsonPropertyName("extensions")]
    public IReadOnlyDictionary<string, object>? Extensions { get; init; }
}
