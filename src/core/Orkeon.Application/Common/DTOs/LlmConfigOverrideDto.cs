using System.Text.Json.Serialization;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Partial LLM-config patch applied on top of an inherited config. Every field is
/// nullable; <c>null</c> means "keep inherited". Mirrors
/// <c>Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride</c>.
/// </summary>
public sealed record LlmConfigOverrideDto
{
    /// <summary>Output-format constraint (<c>"text"</c> | <c>"json_object"</c>).</summary>
    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; init; }

    /// <summary>Sampling temperature override.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>Maximum output tokens override.</summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    /// <summary>Nucleus sampling probability override.</summary>
    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    /// <summary>Thinking-mode toggle / reasoning-effort hint override.</summary>
    [JsonPropertyName("thinking")]
    public LlmThinkingConfigDto? Thinking { get; init; }
}
