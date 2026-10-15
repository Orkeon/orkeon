using System.Text.Json.Serialization;

namespace Orkeon.Application.Task.DTOs;

/// <summary>
/// Task information DTO with comprehensive task data.
/// </summary>
public sealed record TaskDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets or sets the description.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Gets or sets the expected output.</summary>
    [JsonPropertyName("expected_output")]
    public required string ExpectedOutput { get; init; }

    /// <summary>Gets or sets the assigned agent.</summary>
    [JsonPropertyName("assigned_agent")]
    public string? AssignedAgent { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the priority.</summary>
    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    /// <summary>Gets or sets the created at.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
