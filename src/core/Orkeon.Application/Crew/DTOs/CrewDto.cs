using System.Text.Json.Serialization;

namespace Orkeon.Application.Crew.DTOs;

/// <summary>
/// Crew information DTO with team composition and configuration.
/// </summary>
public sealed record CrewDto
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

    /// <summary>Gets or sets the process type.</summary>
    [JsonPropertyName("process_type")]
    public required string ProcessType { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the verbosity.</summary>
    [JsonPropertyName("verbosity")]
    public required string Verbosity { get; init; }

    /// <summary>Gets or sets the created at.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
