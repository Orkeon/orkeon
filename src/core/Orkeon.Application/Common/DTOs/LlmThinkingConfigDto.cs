using System.Text.Json.Serialization;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// DTO mirror of <c>Orkeon.Domain.SharedKernel.ValueObjects.LlmThinkingConfig</c>.
/// Optional per-call thinking-mode toggle (DeepSeek V4 / Anthropic extended thinking).
/// </summary>
public sealed record LlmThinkingConfigDto
{
    /// <summary>Enables (<c>true</c>) or disables (<c>false</c>) the provider's thinking mode.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>Reasoning effort hint forwarded as <c>reasoning_effort</c>. Typical: <c>"low"</c>, <c>"medium"</c>, <c>"high"</c>, <c>"max"</c>.</summary>
    [JsonPropertyName("effort")]
    public string? Effort { get; init; }
}
