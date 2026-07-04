using System.Collections.Immutable;
using System.Text.Json.Serialization;
using static Orkeon.Domain.Constants.Llm.LlmDefaults;
using Orkeon.Domain.Constants.Http;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// LLM configuration DTO with provider settings.
/// </summary>
public sealed record LlmDto
{
    /// <summary>Gets or sets the provider.</summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>Gets or sets the model.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>Gets or sets the temperature.</summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = LlmDefaults.DefaultTemperature;

    /// <summary>Gets or sets the max tokens.</summary>
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = DefaultMaxTokens;

    /// <summary>Gets or sets the max retries.</summary>
    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;

    /// <summary>Gets or sets the timeout.</summary>
    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; init; } = HttpDefaults.DefaultHttpTimeout;

    /// <summary>Parameters.</summary>
    [JsonPropertyName("parameters")]
    public ImmutableDictionary<string, object> Parameters { get; init; } = [];
}
