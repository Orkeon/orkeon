using System.Text.Json.Serialization;
using static Orkeon.Domain.Constants.Llm.LlmDefaults;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// LLM configuration Data Transfer Object.
/// Used for configuring Language Model settings.
/// </summary>
public sealed record LlmConfigDto
{
    /// <summary>
    /// LLM provider name (e.g., "openai", "anthropic", "gemini", "grok", "ollama").
    /// </summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>
    /// Model name (e.g., "gpt-4", "gpt-3.5-turbo", "claude-3-sonnet").
    /// </summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// Temperature setting for response randomness (0.0 to 2.0).
    /// </summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = DefaultTemperature;

    /// <summary>
    /// Maximum tokens in response.
    /// </summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Top-p nucleus sampling parameter (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    /// <summary>
    /// Frequency penalty (-2.0 to 2.0).
    /// </summary>
    [JsonPropertyName("frequency_penalty")]
    public double? FrequencyPenalty { get; init; }

    /// <summary>
    /// Presence penalty (-2.0 to 2.0).
    /// </summary>
    [JsonPropertyName("presence_penalty")]
    public double? PresencePenalty { get; init; }

    /// <summary>
    /// API endpoint URL (for custom providers).
    /// </summary>
    [JsonPropertyName("api_endpoint")]
    public string? ApiEndpoint { get; init; }

    /// <summary>
    /// API key or token (handled securely).
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum requests per minute (rate limiting).
    /// </summary>
    [JsonPropertyName("max_requests_per_minute")]
    public int? MaxRequestsPerMinute { get; init; }

    /// <summary>
    /// Whether to enable streaming responses.
    /// </summary>
    [JsonPropertyName("enable_streaming")]
    public bool EnableStreaming { get; init; }

    /// <summary>
    /// Additional provider-specific settings.
    /// </summary>
    [JsonPropertyName("custom_settings")]
    public Dictionary<string, object> CustomSettings { get; init; } = [];

    /// <summary>
    /// Stop sequences for response generation.
    /// </summary>
    [JsonPropertyName("stop_sequences")]
    public IReadOnlyList<string> StopSequences { get; init; } = [];

    /// <summary>
    /// Whether to enable response caching.
    /// </summary>
    [JsonPropertyName("enable_caching")]
    public bool EnableCaching { get; init; }

    /// <summary>
    /// Cache TTL in minutes.
    /// </summary>
    [JsonPropertyName("cache_ttl_minutes")]
    public int CacheTtlMinutes { get; init; } = 60;

    /// <summary>
    /// Output-format constraint forwarded to providers that implement the
    /// <c>response_format</c> field (DeepSeek today). Accepted: <c>"text"</c>,
    /// <c>"json_object"</c>. <c>null</c> = provider default.
    /// </summary>
    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; init; }
}
