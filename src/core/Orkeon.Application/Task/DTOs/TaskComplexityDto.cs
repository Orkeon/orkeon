using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Task.DTOs;

/// <summary>
/// Task complexity DTO with effort estimation.
/// </summary>
public sealed record TaskComplexityDto
{
    /// <summary>Gets or sets the level.</summary>
    [JsonPropertyName("level")]
    public required string Level { get; init; }

    /// <summary>Gets or sets the estimated effort.</summary>
    [JsonPropertyName("estimated_effort")]
    public int EstimatedEffort { get; init; }

    /// <summary>Gets or sets the estimated duration.</summary>
    [JsonPropertyName("estimated_duration")]
    public TimeSpan EstimatedDuration { get; init; }

    /// <summary>Gets or sets the required skills.</summary>
    [JsonPropertyName("required_skills")]
    public ImmutableList<string> RequiredSkills { get; init; } = [];

    /// <summary>Gets or sets the dependencies.</summary>
    [JsonPropertyName("dependencies")]
    public ImmutableList<string> Dependencies { get; init; } = [];

    /// <summary>Gets or sets the risk factors.</summary>
    [JsonPropertyName("risk_factors")]
    public ImmutableList<string> RiskFactors { get; init; } = [];

    /// <summary>Gets or sets the complexity score.</summary>
    [JsonPropertyName("complexity_score")]
    public double ComplexityScore { get; init; }
}
