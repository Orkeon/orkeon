using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Agent.DTOs;

/// <summary>
/// Agent capabilities DTO with skill and tool information.
/// </summary>
public sealed record AgentCapabilitiesDto
{
    /// <summary>Gets or sets the skills.</summary>
    [JsonPropertyName("skills")]
    public ImmutableList<string> Skills { get; init; } = [];

    /// <summary>Gets or sets the tools.</summary>
    [JsonPropertyName("tools")]
    public ImmutableList<string> Tools { get; init; } = [];

    /// <summary>Gets or sets the languages.</summary>
    [JsonPropertyName("languages")]
    public ImmutableList<string> Languages { get; init; } = [];

    /// <summary>Gets or sets the overall confidence.</summary>
    [JsonPropertyName("overall_confidence")]
    public string OverallConfidence { get; init; } = StatusDefaults.DefaultConfidenceLevel;

    /// <summary>Gets or sets the specializations.</summary>
    [JsonPropertyName("specializations")]
    public ImmutableList<string> Specializations { get; init; } = [];

    /// <summary>Gets or sets the certification level.</summary>
    [JsonPropertyName("certification_level")]
    public string CertificationLevel { get; init; } = StatusDefaults.DefaultCertificationLevel;
}
