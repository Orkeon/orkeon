namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Strongly typed metadata from an LLM provider response.
/// Replaces Dictionary&lt;string, object&gt; usage for LLM response metadata,
/// providing compile-time safety and discoverability for common fields
/// while retaining extensibility via <see cref="ProviderExtensions"/>.
/// </summary>
public sealed record LlmResponseMetadata
{
    /// <summary>
    /// Gets the model that produced the response (e.g., "gpt-4", "claude-3-opus").
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the number of tokens in the prompt / input.
    /// </summary>
    public int? PromptTokens { get; init; }

    /// <summary>
    /// Gets the number of tokens in the completion / output.
    /// </summary>
    public int? CompletionTokens { get; init; }

    /// <summary>
    /// Gets the total number of tokens used (prompt + completion).
    /// </summary>
    public int? TotalTokens { get; init; }

    /// <summary>
    /// Gets the wall-clock latency of the LLM call.
    /// </summary>
    public TimeSpan? Latency { get; init; }

    /// <summary>
    /// Gets the reason the model stopped generating tokens
    /// (e.g., "stop", "length", "tool_calls", "content_filter").
    /// </summary>
    public string? FinishReason { get; init; }

    /// <summary>
    /// Gets the provider-assigned request identifier, useful for debugging and support.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Gets an optional bag of provider-specific metadata that does not map
    /// to any of the common fields above.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ProviderExtensions { get; init; }

    /// <summary>
    /// An empty metadata instance with all fields set to their defaults.
    /// </summary>
    public static readonly LlmResponseMetadata Empty = new();
}
