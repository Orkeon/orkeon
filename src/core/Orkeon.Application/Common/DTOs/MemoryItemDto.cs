using System.Text.Json.Serialization;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Data Transfer Object for memory items.
/// </summary>
public sealed record MemoryItemDto
{
    /// <summary>
    /// Memory item identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Memory content.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Memory item type (experience, fact, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Relevance score.
    /// </summary>
    [JsonPropertyName("relevance")]
    public double Relevance { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Associated agent ID.
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    /// <summary>
    /// Associated task ID.
    /// </summary>
    [JsonPropertyName("task_id")]
    public string? TaskId { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; init; } = [];
}
